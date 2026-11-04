using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectBalance;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace Services.NasbNiro.ProjectBalance
{
    public interface IProjectBalanceService
    {
        Task<ResponseDto<List<BalanceProjectDto>>> GetProjectsAsync(CancellationToken ct);
        Task<ResponseDto<BalanceRunBatchDto>> RunAsync(BalanceRunRequestDto dto, CancellationToken ct);
        Task<ResponseDto<List<BalanceRunListItemDto>>> GetRunsAsync(int workOrderId, CancellationToken ct);
        Task<ResponseDto<BalanceRunDto>> GetRunAsync(int id, CancellationToken ct);
        Task<ResponseDto<BalanceRunDto>> GetLatestAsync(int workOrderId, CancellationToken ct);
        Task<ResponseDto<BalanceDiffDto>> GetDiffAsync(int runId, int? againstRunId, CancellationToken ct);
        Task<ResponseDto<BalanceDraftsDto>> CreateDraftsAsync(int runId, CancellationToken ct);
        Task<ResponseDto<NodeMaterialDto>> GetNodeMaterialAsync(int nodeId, CancellationToken ct);
        Task<ResponseDto<List<BalanceSettingDto>>> GetSettingsAsync(CancellationToken ct);
        Task<ResponseDto<BalanceSettingDto>> SaveSettingAsync(SaveBalanceSettingDto dto, CancellationToken ct);

        /// <summary>The trigger after a project is taken from the bank, a deviation is applied or the WBS is generated: runs the balance (and drafts the requests when the setting says so). It never fails the caller.</summary>
        Task AfterChangeAsync(int workOrderId, string trigger, CancellationToken ct);

        /// <summary>The nightly job: the balance of every current project baseline; returns how many runs were stored.</summary>
        Task<int> RunNightlyAsync(CancellationToken ct);
    }

    /// <summary>
    /// PB2: the automatic material balance of a project. The project copy of the released BOM (all levels, with the approved deviations) is turned into a requirement per item (scrap included), netted against
    /// the company stock, the reservations of the other projects, the open material requests, the open purchase requests and the purchase orders that arrive in time, and the shortage is kept as a run that can be compared with the one before.
    /// The run reserves the stock that is free for the project and uses approved substitutes when the item itself is short. It never submits or approves a purchase request.
    /// </summary>
    public sealed partial class ProjectBalanceService : PbServiceBase, IProjectBalanceService
    {
        public ProjectBalanceService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user)
        {
        }

        private int Me => User.UserId;

        // ------------------------------------------------------------------------------------------------ requirement rows

        private sealed class Req
        {
            public int? ItemId;
            public string PartNumber;
            public string PartName;
            public string Unit;
            public int MakeOrBuy;
            public BalanceRequirementKind Kind;
            public decimal Gross;
            public decimal Scrap;
            public string Note;
            public string AssemblyPartNumber;

            // filled by the WBS mapping
            public int? NodeId;
            public int? ActivityId;
            public DateTime? UseBy;
            public DateTime? Need;
            public int Lead;
            public int? Float;
            public int Priority = 1;
        }

        private sealed class ItemFacts
        {
            public int Id;
            public string Code;
            public string Title;
            public string Unit;
            public string GroupCode;
            public string Material;
            public string Standard;
            public int? PartItemId;
            public bool IsActive;
        }

        private sealed class Pools
        {
            public Dictionary<int, decimal> OnHand = new Dictionary<int, decimal>();
            public Dictionary<int, decimal> ReservedAll = new Dictionary<int, decimal>();
            public Dictionary<int, decimal> ReservedMine = new Dictionary<int, decimal>();
            public Dictionary<int, decimal> OpenRequests = new Dictionary<int, decimal>();
            public Dictionary<int, decimal> SalesHeld = new Dictionary<int, decimal>();
            public Dictionary<int, decimal> PrOpen = new Dictionary<int, decimal>();
            public Dictionary<int, List<PoArrival>> Po = new Dictionary<int, List<PoArrival>>();
            public Dictionary<int, decimal> Issued = new Dictionary<int, decimal>();

            public decimal Get(Dictionary<int, decimal> d, int id) => d.TryGetValue(id, out decimal v) ? v : 0m;

            /// <summary>Stock that is not held by anybody: company stock less every reservation (this project's included), the open material requests of other orders and the sales holds.</summary>
            public decimal Free(int id) => Math.Max(0m, Get(OnHand, id) - Get(ReservedAll, id) - Get(OpenRequests, id) - Get(SalesHeld, id));
        }

        // ------------------------------------------------------------------------------------------------ the run

        private async Task<Dictionary<int, ItemFacts>> LoadItemsAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            List<int> list = ids.Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, ItemFacts>();
            return (await Uow.Set<InventoryItem>().AsNoTracking().Where(i => list.Contains(i.Id))
                .Select(i => new ItemFacts { Id = i.Id, Code = i.Code, Title = i.Title, Unit = i.Unit, GroupCode = i.GroupCode, Material = i.Material, Standard = i.Standard, PartItemId = i.PartItemId, IsActive = i.IsActive && !i.Deleted })
                .ToListAsync(ct)).ToDictionary(i => i.Id);
        }

        private async Task<Pools> LoadPoolsAsync(IEnumerable<int> itemIds, PbProjectBaseline b, int contractId, BalanceSettings s, Dictionary<int, ItemFacts> items, CancellationToken ct)
        {
            List<int> ids = itemIds.Distinct().ToList();
            var pools = new Pools();
            if (ids.Count == 0) return pools;
            WarehouseKind[] kinds = s.WarehouseKinds.Select(k => (WarehouseKind)k).ToArray();

            var onHand = await (from sb in Uow.Set<StockBalance>().AsNoTracking()
                                join w in Uow.Set<Warehouse>().AsNoTracking() on sb.WarehouseId equals w.Id
                                where ids.Contains(sb.ItemId) && !w.Deleted && w.IsActive && kinds.Contains(w.Kind)
                                select new { sb.ItemId, sb.Quantity }).ToListAsync(ct);
            pools.OnHand = onHand.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var res = await Uow.Set<ProjectReservation>().AsNoTracking().Where(r => !r.Deleted && r.Status == ReservationStatus.Active && ids.Contains(r.ItemId))
                .Select(r => new { r.ItemId, r.BaselineId, r.Quantity, r.IssuedQuantity }).ToListAsync(ct);
            pools.ReservedAll = res.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => Math.Max(0m, r.Quantity - r.IssuedQuantity)));
            pools.ReservedMine = res.Where(r => r.BaselineId == b.Id).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => Math.Max(0m, r.Quantity - r.IssuedQuantity)));

            // what open material requests of other orders still have to issue (the requests of this work order are this project's own demand)
            var req = await (from l in Uow.Set<MaterialRequestLine>().AsNoTracking()
                             join r in Uow.Set<MaterialRequest>().AsNoTracking() on l.MaterialRequestId equals r.Id
                             where ids.Contains(l.ItemId) && !r.Deleted && (r.Status == MaterialRequestStatus.Submitted || r.Status == MaterialRequestStatus.Approved)
                                   && (r.WorkOrderId == null || r.WorkOrderId != b.WorkOrderId)
                             select new { l.ItemId, l.Quantity, l.IssuedQuantity }).ToListAsync(ct);
            pools.OpenRequests = req.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => Math.Max(0m, x.Quantity - x.IssuedQuantity)));
            pools.SalesHeld = await Sales.SlReservations.ReservedAsync(Uow, ids, ct);

            // open purchase requests (submitted or approved, of this project or general) less what is already on orders
            var prLines = await (from l in Uow.Set<PurchaseRequestLine>().AsNoTracking()
                                 join r in Uow.Set<PurchaseRequest>().AsNoTracking() on l.PurchaseRequestId equals r.Id
                                 where l.ItemId != null && ids.Contains(l.ItemId.Value) && !r.Deleted && (r.Status == PurchaseRequestStatus.Submitted || r.Status == PurchaseRequestStatus.Approved)
                                       && (r.ContractId == null || r.ContractId == contractId)
                                 select new { LineId = l.Id, ItemId = l.ItemId.Value, l.Quantity }).ToListAsync(ct);
            List<int> prLineIds = prLines.Select(l => l.LineId).ToList();
            var ordered = prLineIds.Count == 0 ? new Dictionary<int, decimal>() : (await (from l in Uow.Set<PurchaseOrderLine>().AsNoTracking()
                                                                                      join p in Uow.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.Id
                                                                                      where l.RequestLineId != null && prLineIds.Contains(l.RequestLineId.Value) && !p.Deleted && p.Status != PurchaseOrderStatus.Cancelled
                                                                                      select new { LineId = l.RequestLineId.Value, l.Quantity }).ToListAsync(ct))
                .GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            pools.PrOpen = prLines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => Math.Max(0m, l.Quantity - (ordered.TryGetValue(l.LineId, out decimal o) ? o : 0m))));

            // purchase orders that are placed (approved, sent, partly received) and still to arrive; an order of another project is not ours
            PurchaseOrderStatus[] open = { PurchaseOrderStatus.Approved, PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived };
            var po = await (from l in Uow.Set<PurchaseOrderLine>().AsNoTracking()
                            join p in Uow.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.Id
                            where l.ItemId != null && ids.Contains(l.ItemId.Value) && !p.Deleted && open.Contains(p.Status)
                                  && ((l.ContractId ?? p.ContractId) == null || (l.ContractId ?? p.ContractId) == contractId)
                            select new { ItemId = l.ItemId.Value, Left = l.Quantity - l.ReceivedQuantity, Delivery = l.DeliveryDate ?? p.DeliveryDate, p.OrderDate }).ToListAsync(ct);
            foreach (var row in po.Where(x => x.Left > 0))
            {
                // no delivery date on the order: the order date plus the lead time of the item (answers table)
                DateTime arrival = row.Delivery ?? row.OrderDate.AddDays(items.TryGetValue(row.ItemId, out ItemFacts f) ? s.LeadDays(f.GroupCode) : s.LeadDefaultDays);
                if (!pools.Po.TryGetValue(row.ItemId, out List<PoArrival> list)) pools.Po[row.ItemId] = list = new List<PoArrival>();
                list.Add(new PoArrival { Date = arrival.Date, Quantity = row.Left });
            }

            // issued to the work order (confirmed issues less returns from production)
            var issued = await (from dl in Uow.Set<InventoryDocumentLine>().AsNoTracking()
                                join d in Uow.Set<InventoryDocument>().AsNoTracking() on dl.DocumentId equals d.Id
                                where d.WorkOrderId == b.WorkOrderId && !d.Deleted && d.Status == InventoryDocumentStatus.Confirmed && ids.Contains(dl.ItemId)
                                      && (d.Type == InventoryDocumentType.Issue || d.Type == InventoryDocumentType.ProductionReturn)
                                select new { dl.ItemId, d.Type, dl.Quantity }).ToListAsync(ct);
            pools.Issued = issued.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => Math.Max(0m, g.Sum(x => x.Type == InventoryDocumentType.Issue ? x.Quantity : -x.Quantity)));
            return pools;
        }

        /// <summary>The requirement per item: a bought part is the item itself, a made part is its raw material; assemblies are not ordered, their parts are.</summary>
        private async Task<(List<Req> Rows, int CustomerSupplied)> ExplodeAsync(PbProjectBaseline b, CancellationToken ct)
        {
            List<PbProjectBomLine> lines = await Uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => l.BaselineId == b.Id && !l.Deleted && !l.IsHeading && !l.IsRemoved).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToListAsync(ct);
            List<int> srcIds = lines.Where(l => l.SourceLineId.HasValue).Select(l => l.SourceLineId.Value).Distinct().ToList();
            Dictionary<int, PbBomLine> src = srcIds.Count == 0 ? new Dictionary<int, PbBomLine>() : await Uow.Set<PbBomLine>().AsNoTracking().Where(l => srcIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

            // the first stockable stage of the OPC chart of each part is where its raw material enters the stock
            List<int> routingIds = await Uow.Set<PbProjectRouting>().AsNoTracking().Where(r => r.BaselineId == b.Id && !r.Deleted).Select(r => r.OpcRoutingId).ToListAsync(ct);
            var steps = routingIds.Count == 0 ? null : await Uow.Set<PbOpcStep>().AsNoTracking().Where(st => routingIds.Contains(st.RoutingId) && !st.Deleted && st.StageItemId != null)
                .OrderBy(st => st.RoutingId).ThenBy(st => st.Seq).Select(st => new { st.PartNumber, st.Seq, st.StageItemId }).ToListAsync(ct);
            var firstStage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (steps != null)
                foreach (var g in steps.Where(x => x.PartNumber != null).GroupBy(x => x.PartNumber, StringComparer.OrdinalIgnoreCase))
                    firstStage[g.Key] = g.OrderBy(x => x.Seq).First().StageItemId.Value;

            List<string> partNumbers = lines.Where(l => l.ItemId == null && l.PartNumber != null).Select(l => l.PartNumber).Distinct().ToList();
            Dictionary<string, int> partItems = partNumbers.Count == 0 ? new Dictionary<string, int>() : (await Uow.Set<InventoryItem>().AsNoTracking()
                .Where(i => !i.Deleted && i.StageCode2 == null && i.PartNumber != null && partNumbers.Contains(i.PartNumber)).Select(i => new { i.PartNumber, i.Id }).ToListAsync(ct))
                .GroupBy(x => x.PartNumber).ToDictionary(g => g.Key, g => g.Min(x => x.Id));

            var wanted = new HashSet<int>(lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value));
            foreach (int id in firstStage.Values) wanted.Add(id);
            foreach (int id in partItems.Values) wanted.Add(id);
            Dictionary<int, ItemFacts> items = await LoadItemsAsync(wanted, ct);

            // BOM material with no OPC stage item: the one active raw item (group 01) with exactly that material
            List<string> materials = lines.Where(l => l.MakeOrBuy != PbMakeOrBuy.Purchase && l.MakeOrBuy != PbMakeOrBuy.CustomerSupplied && !string.IsNullOrWhiteSpace(l.Material)).Select(l => l.Material.Trim()).Distinct().ToList();
            Dictionary<string, List<ItemFacts>> byMaterial = new Dictionary<string, List<ItemFacts>>(StringComparer.OrdinalIgnoreCase);
            if (materials.Count > 0)
            {
                var raws = await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.IsActive && i.GroupCode == "01" && i.Material != null && materials.Contains(i.Material))
                    .Select(i => new ItemFacts { Id = i.Id, Code = i.Code, Title = i.Title, Unit = i.Unit, GroupCode = i.GroupCode, Material = i.Material, IsActive = true }).ToListAsync(ct);
                foreach (ItemFacts r in raws) { if (!byMaterial.TryGetValue(r.Material.Trim(), out List<ItemFacts> l)) byMaterial[r.Material.Trim()] = l = new List<ItemFacts>(); l.Add(r); }
            }

            var rows = new List<Req>();
            int customerSupplied = 0;
            foreach (PbProjectBomLine l in lines)
            {
                src.TryGetValue(l.SourceLineId ?? 0, out PbBomLine sl);
                // an assembly (it has a child BOM) is built from its parts, which follow it in the project copy
                if (sl != null && sl.ChildBomId.HasValue) continue;
                if (l.MakeOrBuy == PbMakeOrBuy.CustomerSupplied) { customerSupplied++; continue; }
                decimal qty = l.TotalQty;
                if (qty <= 0) continue;
                string pn = l.PartNumber;

                if (l.MakeOrBuy == PbMakeOrBuy.Purchase)
                {
                    int? itemId = l.ItemId ?? (pn != null && partItems.TryGetValue(pn, out int pi) ? pi : (int?)null);
                    decimal factor = BalanceRules.ScrapFactor(sl?.ScrapFactor, null, null);
                    (decimal gross, decimal scrap) = BalanceRules.Requirement(qty, factor);
                    if (itemId == null || !items.ContainsKey(itemId.Value))
                    {
                        rows.Add(new Req { Kind = BalanceRequirementKind.NotCoded, PartNumber = pn, PartName = l.PartName, Unit = l.Unit, MakeOrBuy = (int)l.MakeOrBuy, Gross = gross, Scrap = scrap, AssemblyPartNumber = l.AssemblyPartNumber, Note = "کالای این شماره قطعه کدگذاری نشده است؛ پس از کدگذاری دوباره اجرا کنید." });
                        continue;
                    }
                    rows.Add(new Req { Kind = BalanceRequirementKind.Purchased, ItemId = itemId, PartNumber = pn, PartName = l.PartName, Unit = items[itemId.Value].Unit ?? l.Unit, MakeOrBuy = (int)l.MakeOrBuy, Gross = gross, Scrap = scrap, AssemblyPartNumber = l.AssemblyPartNumber });
                    continue;
                }

                // made (or made by a contractor from our material): its raw material
                ItemFacts raw = null;
                string how = null;
                if (pn != null && firstStage.TryGetValue(pn, out int stageItem) && items.TryGetValue(stageItem, out raw)) how = "مرحله اول زنجیره OPC";
                else if (!string.IsNullOrWhiteSpace(l.Material) && byMaterial.TryGetValue(l.Material.Trim(), out List<ItemFacts> same) && same.Count == 1) { raw = same[0]; how = "ماده BOM"; }
                if (raw == null)
                {
                    decimal f0 = BalanceRules.ScrapFactor(sl?.ScrapFactor, null, null);
                    (decimal g0, decimal s0) = BalanceRules.Requirement(qty, f0);
                    rows.Add(new Req { Kind = BalanceRequirementKind.NotCoded, PartNumber = pn, PartName = l.PartName, Unit = l.Unit, MakeOrBuy = (int)l.MakeOrBuy, Gross = g0, Scrap = s0, AssemblyPartNumber = l.AssemblyPartNumber, Note = "مواد اولیه این قطعه کدگذاری نشده است (ماده: " + (l.Material ?? "-") + ")." });
                    continue;
                }
                decimal? net = sl?.NetWeight, grossWeight = sl?.GrossWeight;
                bool byWeight = BalanceRules.IsWeightUnit(raw.Unit) && (net.HasValue || grossWeight.HasValue);
                decimal baseQty, f;
                string note = how;
                if (byWeight)
                {
                    baseQty = qty * (net ?? grossWeight.Value);
                    f = BalanceRules.ScrapFactor(sl?.ScrapFactor, net, grossWeight);
                    note += "؛ بر پایه وزن";
                }
                else
                {
                    baseQty = qty;
                    f = BalanceRules.ScrapFactor(sl?.ScrapFactor, null, null);
                    if (BalanceRules.IsWeightUnit(raw.Unit)) note += "؛ وزن قطعه در BOM نیست و تعداد شمرده شد";
                }
                (decimal gross2, decimal scrap2) = BalanceRules.Requirement(baseQty, f);
                rows.Add(new Req { Kind = BalanceRequirementKind.RawOfMake, ItemId = raw.Id, PartNumber = pn, PartName = l.PartName, Unit = raw.Unit ?? l.Unit, MakeOrBuy = (int)l.MakeOrBuy, Gross = gross2, Scrap = scrap2, AssemblyPartNumber = l.AssemblyPartNumber, Note = "ماده اولیه: " + raw.Code + " (" + note + ")" });
            }

            // the same item of the same part from two lines is one row
            var merged = new List<Req>();
            var seen = new Dictionary<(int?, string, BalanceRequirementKind), Req>();
            foreach (Req r in rows)
            {
                var key = (r.ItemId, r.PartNumber, r.Kind);
                if (seen.TryGetValue(key, out Req first)) { first.Gross = BalanceRules.Round(first.Gross + r.Gross); first.Scrap = BalanceRules.Round(first.Scrap + r.Scrap); }
                else { seen[key] = r; merged.Add(r); }
            }
            return (merged, customerSupplied);
        }

        /// <summary>Finds the part node of the WBS of the work order and its procurement (P) and construction (C) activities, and sets the dates, float and priority of each requirement.</summary>
        private async Task MapWbsAsync(List<Req> rows, PbProjectBaseline b, int contractId, BalanceSettings s, Dictionary<int, ItemFacts> items, CancellationToken ct)
        {
            var acts = await Uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && a.WorkOrderId == b.WorkOrderId && !a.Deleted && a.WbsNodeId != null)
                .Select(a => new { a.Id, a.Code, NodeId = a.WbsNodeId.Value, a.Phase, a.EarlyStart, a.EarlyFinish, a.LateStart, a.ForecastStart, a.TotalFloat, a.IsCritical }).ToListAsync(ct);
            if (acts.Count == 0) { foreach (Req r in rows) FinishDates(r, null, null, s, items); return; }
            List<int> nodeIds = acts.Select(a => a.NodeId).Distinct().ToList();
            var nodes = await Uow.Set<PcWbsNode>().AsNoTracking().Where(n => nodeIds.Contains(n.Id) && !n.Deleted).Select(n => new { n.Id, n.Title }).ToListAsync(ct);
            var nodeOfPart = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes.OrderBy(n => n.Id))
            {
                string pn = PbMspdi.PartNumberOf(n.Title);
                if (pn != null && !nodeOfPart.ContainsKey(pn)) nodeOfPart[pn] = n.Id;
            }
            foreach (Req r in rows)
            {
                int? nodeId = null;
                if (r.PartNumber != null && nodeOfPart.TryGetValue(r.PartNumber, out int n1)) nodeId = n1;
                else if (r.AssemblyPartNumber != null && nodeOfPart.TryGetValue(r.AssemblyPartNumber, out int n2)) nodeId = n2;
                if (nodeId == null) { FinishDates(r, null, null, s, items); continue; }
                var p = acts.Where(a => a.NodeId == nodeId.Value && a.Phase == PcPhase.Procurement).OrderBy(a => a.Id).FirstOrDefault();
                var c = acts.Where(a => a.NodeId == nodeId.Value && a.Phase == PcPhase.Construction).OrderBy(a => a.Id).FirstOrDefault();
                r.NodeId = nodeId;
                r.ActivityId = p?.Id;
                DateTime? useBy = c?.EarlyStart ?? c?.LateStart ?? c?.ForecastStart ?? p?.EarlyFinish;
                r.Float = p?.TotalFloat;
                FinishDates(r, useBy, p == null ? (bool?)null : p.IsCritical, s, items);
            }
        }

        private static void FinishDates(Req r, DateTime? useBy, bool? critical, BalanceSettings s, Dictionary<int, ItemFacts> items)
        {
            string group = r.ItemId.HasValue && items.TryGetValue(r.ItemId.Value, out ItemFacts f) ? f.GroupCode : null;
            r.Lead = r.ItemId.HasValue ? s.LeadDays(group) : 0;
            r.UseBy = useBy?.Date;
            r.Need = useBy?.Date.AddDays(-r.Lead);
            r.Priority = BalanceRules.Priority(r.Float, critical == true, s.FloatUrgentDays, s.FloatHighDays);
        }

        private sealed class RunOutcome
        {
            public ProjectMaterialBalance Run;
            public List<ProjectMaterialBalanceLine> Lines = new List<ProjectMaterialBalanceLine>();
            public bool Stored;
            public string Note;
        }

        /// <summary>One run for one baseline. Reserves what is free (when the setting says so), stores the run and its lines, and returns them. No role check: the callers check the caller.</summary>
        private async Task<RunOutcome> ExecuteAsync(PbProjectBaseline b, string trigger, BalanceSettings s, CancellationToken ct)
        {
            WorkOrder wo = await Uow.Set<WorkOrder>().AsNoTracking().FirstAsync(w => w.Id == b.WorkOrderId, ct);
            int contractId = wo.ContractId;
            (List<Req> rows, int customerSupplied) = await ExplodeAsync(b, ct);

            var itemIds = new HashSet<int>(rows.Where(r => r.ItemId.HasValue).Select(r => r.ItemId.Value));
            Dictionary<int, ItemFacts> items = await LoadItemsAsync(itemIds, ct);
            await MapWbsAsync(rows, b, contractId, s, items, ct);

            // approved substitutes of the items (valid today, global or for this project)
            DateTime today = DateTime.Today;
            List<ItemSubstitute> subs = !s.UseSubstitutes || itemIds.Count == 0 ? new List<ItemSubstitute>() : await Uow.Set<ItemSubstitute>().AsNoTracking()
                .Where(x => !x.Deleted && x.Status == SubstituteStatus.Approved && itemIds.Contains(x.ItemId) && (x.ValidFrom == null || x.ValidFrom <= today) && (x.ValidTo == null || x.ValidTo >= today)
                            && (x.Scope == SubstituteScope.Global || (x.ContractId == contractId && (x.WorkOrderId == null || x.WorkOrderId == b.WorkOrderId)))).OrderBy(x => x.Id).ToListAsync(ct);
            Dictionary<int, ItemFacts> subItems = await LoadItemsAsync(subs.Select(x => x.SubstituteItemId), ct);
            foreach (var kv in subItems) items[kv.Key] = kv.Value;

            Pools pools = await LoadPoolsAsync(itemIds.Concat(subs.Select(x => x.SubstituteItemId)), b, contractId, s, items, ct);

            // allocation per item, earliest need first
            var lines = new List<ProjectMaterialBalanceLine>();
            var newReservations = new Dictionary<int, (decimal Qty, Req First)>();
            var substituteReservations = new Dictionary<(int Sub, int For), (decimal Qty, Req First)>();
            var subUsed = new Dictionary<int, decimal>();
            int sort = 0;
            foreach (var group in rows.Where(r => r.ItemId.HasValue).GroupBy(r => r.ItemId.Value).OrderBy(g => g.Key))
            {
                int id = group.Key;
                List<Req> ordered = group.OrderBy(r => r.UseBy ?? DateTime.MaxValue).ThenBy(r => r.PartNumber, StringComparer.Ordinal).ToList();
                var demand = ordered.Select(r => new DemandRow { Required = BalanceRules.Round(r.Gross + r.Scrap), UseBy = r.UseBy }).ToList();
                List<RowAllocation> alloc = BalanceRules.Allocate(demand, pools.Get(pools.Issued, id), pools.Get(pools.ReservedMine, id), pools.Free(id), s.ReserveOnRun, pools.Get(pools.PrOpen, id),
                    pools.Po.TryGetValue(id, out List<PoArrival> po) ? po : null);
                decimal onHand = pools.Get(pools.OnHand, id), available = pools.Free(id);
                for (int i = 0; i < ordered.Count; i++)
                {
                    Req r = ordered[i];
                    RowAllocation a = alloc[i];
                    var line = new ProjectMaterialBalanceLine
                    {
                        Kind = r.Kind, ItemId = id, PartNumber = r.PartNumber, PartName = Cut(r.PartName, 300), Unit = r.Unit, MakeOrBuy = r.MakeOrBuy, WbsNodeId = r.NodeId, ActivityId = r.ActivityId,
                        NeedDate = r.Need, UseByDate = r.UseBy, LeadTimeDays = r.Lead, TotalFloat = r.Float, Priority = r.Priority,
                        GrossQty = r.Gross, ScrapQty = r.Scrap, RequiredQty = demand[i].Required, IssuedQty = BalanceRules.Round(a.Issued), ReservedQty = BalanceRules.Round(a.Reserved), ReservedNewQty = BalanceRules.Round(a.ReservedNew),
                        OnHandQty = onHand, AvailableQty = available, OnOrderPrQty = BalanceRules.Round(a.Pr), OnOrderPoQty = BalanceRules.Round(a.Po), ShortQty = a.Short, Note = r.Note,
                    };
                    if (a.ReservedNew > 0)
                    {
                        newReservations.TryGetValue(id, out var cur);
                        newReservations[id] = (cur.Qty + a.ReservedNew, cur.First ?? r);
                    }
                    // the item itself is short: an approved substitute with free stock covers what it can
                    if (a.Short > 0)
                    {
                        foreach (ItemSubstitute sub in subs.Where(x => x.ItemId == id))
                        {
                            if (line.ShortQty <= 0) break;
                            decimal subFree = Math.Max(0m, pools.Free(sub.SubstituteItemId) - (subUsed.TryGetValue(sub.SubstituteItemId, out decimal used) ? used : 0m));
                            (decimal covered, decimal units) = BalanceRules.Substitute(line.ShortQty, sub.Ratio, subFree);
                            if (covered <= 0) continue;
                            subUsed[sub.SubstituteItemId] = (subUsed.TryGetValue(sub.SubstituteItemId, out decimal u2) ? u2 : 0m) + units;
                            line.ShortQty = BalanceRules.Round(line.ShortQty - covered);
                            line.SubstituteQty = BalanceRules.Round(line.SubstituteQty + units);
                            line.SubstituteItemId = line.SubstituteItemId ?? sub.SubstituteItemId;
                            string code = subItems.TryGetValue(sub.SubstituteItemId, out ItemFacts sf) ? sf.Code : "#" + sub.SubstituteItemId;
                            line.Note = (line.Note == null ? "" : line.Note + " | ") + "جایگزین تاییدشده " + code + ": " + BalanceRules.Num(units) + " " + (subItems.TryGetValue(sub.SubstituteItemId, out ItemFacts sf2) ? sf2.Unit : "") + " به‌جای " + BalanceRules.Num(covered) + " (نسبت " + BalanceRules.Num(sub.Ratio) + ")";
                            if (s.ReserveOnRun)
                            {
                                var key = (sub.SubstituteItemId, id);
                                substituteReservations.TryGetValue(key, out var cs);
                                substituteReservations[key] = (cs.Qty + units, cs.First ?? r);
                            }
                        }
                    }
                    lines.Add(line);
                }
            }
            // rows with no item code are listed and flagged
            foreach (Req r in rows.Where(r => !r.ItemId.HasValue))
                lines.Add(new ProjectMaterialBalanceLine
                {
                    Kind = r.Kind, PartNumber = r.PartNumber, PartName = Cut(r.PartName, 300), Unit = r.Unit, MakeOrBuy = r.MakeOrBuy, WbsNodeId = r.NodeId, ActivityId = r.ActivityId, NeedDate = r.Need, UseByDate = r.UseBy,
                    LeadTimeDays = r.Lead, TotalFloat = r.Float, Priority = r.Priority, GrossQty = r.Gross, ScrapQty = r.Scrap, RequiredQty = BalanceRules.Round(r.Gross + r.Scrap), Note = r.Note,
                });

            lines = lines.OrderBy(l => l.UseByDate ?? DateTime.MaxValue).ThenBy(l => l.PartNumber, StringComparer.Ordinal).ThenBy(l => l.ItemId ?? 0).ToList();
            foreach (ProjectMaterialBalanceLine l in lines) l.SortOrder = ++sort;

            // identical to the run before (nothing changed since last night): a nightly run is not stored again
            List<string> hashParts = lines.Select(l => string.Join("|", l.ItemId, l.PartNumber, l.WbsNodeId, l.Kind, BalanceRules.Num(l.RequiredQty), BalanceRules.Num(l.IssuedQty), BalanceRules.Num(l.ReservedQty), BalanceRules.Num(l.OnOrderPrQty),
                BalanceRules.Num(l.OnOrderPoQty), BalanceRules.Num(l.SubstituteQty), BalanceRules.Num(l.ShortQty), l.NeedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ToList();
            string hash = BalanceRules.Hash(hashParts);
            ProjectMaterialBalance previous = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(r => r.BaselineId == b.Id && !r.Deleted).OrderByDescending(r => r.RunNumber).FirstOrDefaultAsync(ct);
            var outcome = new RunOutcome();
            if (trigger == "Nightly" && previous != null && previous.ContentHash == hash && newReservations.Count == 0 && substituteReservations.Count == 0)
            {
                outcome.Run = previous; outcome.Stored = false; outcome.Note = "بدون تغییر نسبت به اجرای قبل؛ اجرای تازه ذخیره نشد.";
                return outcome;
            }

            var run = new ProjectMaterialBalance
            {
                ContractId = contractId, WorkOrderId = b.WorkOrderId, BaselineId = b.Id, BomHeaderId = b.BomHeaderId, BomCode = b.SourceBomCode, BomRevision = b.SourceBomRevision,
                RunNumber = (previous?.RunNumber ?? 0) + 1, RunAt = DateTimeOffset.Now, RunByUserId = Me, Trigger = trigger, SettingsJson = s.ToJson(), ContentHash = hash, PreviousRunId = previous?.Id,
                LineCount = lines.Count, ShortLineCount = lines.Count(l => l.ShortQty > 0), NotCodedCount = lines.Count(l => l.Kind == BalanceRequirementKind.NotCoded),
                Note = customerSupplied > 0 ? customerSupplied + " ردیف تامین‌شده توسط کارفرما شمرده نشد." : null,
            };
            Stamp(run, true);
            Uow.Set<ProjectMaterialBalance>().Add(run);
            await Uow.SaveChangesAsync(ct);
            foreach (ProjectMaterialBalanceLine l in lines) { l.BalanceId = run.Id; Stamp(l, true); }
            Uow.Set<ProjectMaterialBalanceLine>().AddRange(lines);

            await UpsertReservationsAsync(b, contractId, s, newReservations, substituteReservations, run, ct);
            await Uow.SaveChangesAsync(ct);
            outcome.Run = run; outcome.Lines = lines; outcome.Stored = true;
            return outcome;
        }

        private async Task UpsertReservationsAsync(PbProjectBaseline b, int contractId, BalanceSettings s, Dictionary<int, (decimal Qty, Req First)> primary, Dictionary<(int Sub, int For), (decimal Qty, Req First)> substitutes,
            ProjectMaterialBalance run, CancellationToken ct)
        {
            if (primary.Count == 0 && substitutes.Count == 0) return;
            List<ProjectReservation> existing = await Uow.Set<ProjectReservation>().Where(r => r.BaselineId == b.Id && !r.Deleted && r.Status == ReservationStatus.Active && r.LotId == null && r.SerialUnitId == null).ToListAsync(ct);
            void Upsert(int itemId, int? substituteFor, decimal qty, Req first)
            {
                ProjectReservation r = existing.FirstOrDefault(x => x.ItemId == itemId && x.SubstituteForItemId == substituteFor);
                if (r != null)
                {
                    r.Quantity = BalanceRules.Round(r.Quantity + qty);
                    r.SourceBalanceId = run.Id;
                    Stamp(r, false);
                    return;
                }
                r = new ProjectReservation
                {
                    ContractId = contractId, WorkOrderId = b.WorkOrderId, BaselineId = b.Id, ItemId = itemId, Quantity = BalanceRules.Round(qty), Status = ReservationStatus.Active, IsHard = s.ReservationHard,
                    NeedDate = first?.UseBy, WbsNodeId = first?.NodeId, ActivityId = first?.ActivityId, SubstituteForItemId = substituteFor, Reason = "بالانس خودکار مواد (اجرای " + run.RunNumber + ")",
                };
                r.SourceBalanceId = run.Id;
                Stamp(r, true);
                Uow.Set<ProjectReservation>().Add(r);
                existing.Add(r);
            }
            foreach (var kv in primary) Upsert(kv.Key, null, kv.Value.Qty, kv.Value.First);
            foreach (var kv in substitutes) Upsert(kv.Key.Sub, kv.Key.For, kv.Value.Qty, kv.Value.First);
        }

        private static string Cut(string text, int max) => text == null ? null : text.Length <= max ? text : text.Substring(0, max);

        // ------------------------------------------------------------------------------------------------ the entry points

        public async Task<ResponseDto<BalanceRunBatchDto>> RunAsync(BalanceRunRequestDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.Planning)) return ResponseDtoHelper.BadRequest<BalanceRunBatchDto>(BalanceMessages.NotAllowed);
            if (dto == null || (!dto.WorkOrderId.HasValue && !dto.ContractId.HasValue)) return ResponseDtoHelper.BadRequest<BalanceRunBatchDto>(BalanceMessages.InvalidData);
            IQueryable<PbProjectBaseline> q = Uow.Set<PbProjectBaseline>().Where(x => !x.Deleted && x.IsCurrent);
            if (dto.WorkOrderId.HasValue) q = q.Where(x => x.WorkOrderId == dto.WorkOrderId.Value);
            else
            {
                int cid = dto.ContractId.Value;
                q = q.Where(x => Uow.Set<WorkOrder>().Any(w => w.Id == x.WorkOrderId && w.ContractId == cid));
            }
            List<PbProjectBaseline> baselines = await q.OrderBy(x => x.WorkOrderId).ToListAsync(ct);
            if (baselines.Count == 0) return ResponseDtoHelper.BadRequest<BalanceRunBatchDto>(BalanceMessages.NoBaseline);
            BalanceSettings s = await BalanceSettingKeys.LoadAsync(Uow, ct);
            var batch = new BalanceRunBatchDto();
            foreach (PbProjectBaseline b in baselines)
            {
                RunOutcome outcome = await ExecuteAsync(b, "Manual", s, ct);
                batch.Runs.Add(await RunDtoAsync(outcome.Run.Id, true, ct));
                if (dto.CreateDrafts)
                {
                    ResponseDto<BalanceDraftsDto> drafts = await DraftsCoreAsync(outcome.Run.Id, s, ct);
                    if (drafts.Success) batch.Drafts.Add(drafts.Data); else batch.Notes.Add(drafts.Message);
                }
            }
            return ResponseDtoHelper.Ok(batch);
        }

        public async Task AfterChangeAsync(int workOrderId, string trigger, CancellationToken ct)
        {
            try
            {
                BalanceSettings s = await BalanceSettingKeys.LoadAsync(Uow, ct);
                if (!s.AutoRunOnChange) return;
                PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().FirstOrDefaultAsync(x => x.WorkOrderId == workOrderId && !x.Deleted && x.IsCurrent, ct);
                if (b == null) return;
                RunOutcome outcome = await ExecuteAsync(b, trigger, s, ct);
                if (s.AutoDraftRequests && outcome.Run != null) await DraftsCoreAsync(outcome.Run.Id, s, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // the balance is a help: whatever made it fail must not undo the instantiation or the deviation that triggered it
                if (Uow is DbContext db) db.ChangeTracker.Clear();
            }
        }

        public async Task<int> RunNightlyAsync(CancellationToken ct)
        {
            BalanceSettings s = await BalanceSettingKeys.LoadAsync(Uow, ct);
            if (!s.NightlyEnabled) return 0;
            List<int> ids = await Uow.Set<PbProjectBaseline>().AsNoTracking().Where(x => !x.Deleted && x.IsCurrent).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
            int stored = 0;
            foreach (int id in ids)
            {
                try
                {
                    PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().FirstAsync(x => x.Id == id, ct);
                    RunOutcome outcome = await ExecuteAsync(b, "Nightly", s, ct);
                    if (outcome.Stored) stored++;
                    if (s.AutoDraftRequests && outcome.Stored) await DraftsCoreAsync(outcome.Run.Id, s, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { if (Uow is DbContext db) db.ChangeTracker.Clear(); }
            }
            return stored;
        }
    }
}
