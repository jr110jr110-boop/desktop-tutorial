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
            public bool UnitMismatch;

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
            /// <summary>What other work orders hold of an item: per work order the larger of its reservations and its open material requests (a kit is drafted from a reservation, so the two are one claim), summed.</summary>
            public Dictionary<int, decimal> HeldOthers = new Dictionary<int, decimal>();
            /// <summary>All the reservations of this project that are still open, as stock of the reserved item (a substitute's stock included).</summary>
            public Dictionary<int, decimal> ReservedMineAll = new Dictionary<int, decimal>();
            /// <summary>This project's reservations of the item for itself (not as a substitute of another item).</summary>
            public Dictionary<int, decimal> ReservedMinePrimary = new Dictionary<int, decimal>();
            /// <summary>Stock of a substitute that is reserved for this project in place of the item: item -> (substitute, units of the substitute).</summary>
            public Dictionary<int, List<(int Sub, decimal Qty)>> Credits = new Dictionary<int, List<(int Sub, decimal Qty)>>();
            /// <summary>Reserved item (and the item it replaces) whose reservation was released: no automatic reservation until a planner enables it again.</summary>
            public HashSet<(int Item, int? For)> Blocked = new HashSet<(int Item, int? For)>();
            public Dictionary<int, decimal> SalesHeld = new Dictionary<int, decimal>();
            public Dictionary<int, decimal> PrOpen = new Dictionary<int, decimal>();
            public Dictionary<int, List<PoArrival>> Po = new Dictionary<int, List<PoArrival>>();
            public Dictionary<int, decimal> Issued = new Dictionary<int, decimal>();

            public decimal Get(Dictionary<int, decimal> d, int id) => d.TryGetValue(id, out decimal v) ? v : 0m;

            /// <summary>Stock that is not held by anybody: company stock less the claims of the other work orders, this project's own reservations and the sales holds.</summary>
            public decimal Free(int id) => Math.Max(0m, Get(OnHand, id) - Get(HeldOthers, id) - Get(ReservedMineAll, id) - Get(SalesHeld, id));
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

        /// <summary>The statuses of a purchase order that is on its way: a draft order already holds the quantity (so it is in both the "ordered against the request" and the "arriving" figures, never in neither).</summary>
        internal static readonly PurchaseOrderStatus[] ArrivingStatuses = { PurchaseOrderStatus.Draft, PurchaseOrderStatus.Approved, PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived };

        private async Task<Pools> LoadPoolsAsync(IEnumerable<int> itemIds, PbProjectBaseline b, int contractId, BalanceSettings s, Dictionary<int, ItemFacts> items, CancellationToken ct)
        {
            List<int> ids = itemIds.Distinct().ToList();
            var pools = new Pools();
            if (ids.Count == 0) return pools;
            WarehouseKind[] kinds = s.WarehouseKinds.Select(k => (WarehouseKind)k).ToArray();

            // company stock of the counted warehouses; customer property (lots marked so) is not ours
            var onHand = await (from sb in Uow.Set<StockBalance>().AsNoTracking()
                                join w in Uow.Set<Warehouse>().AsNoTracking() on sb.WarehouseId equals w.Id
                                where ids.Contains(sb.ItemId) && !w.Deleted && w.IsActive && kinds.Contains(w.Kind) && w.WorkOrderId == null && (sb.Lot == null || !sb.Lot.IsCustomerProperty)
                                select new { sb.ItemId, sb.Quantity }).ToListAsync(ct);
            pools.OnHand = onHand.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var res = await Uow.Set<ProjectReservation>().AsNoTracking().Where(r => !r.Deleted && (r.Status == ReservationStatus.Active || (r.Status == ReservationStatus.Released && r.AutoReserveBlocked && r.BaselineId == b.Id)) && ids.Contains(r.ItemId))
                .Select(r => new { r.ItemId, r.BaselineId, r.WorkOrderId, r.Status, r.SubstituteForItemId, Left = r.Quantity - r.IssuedQuantity }).ToListAsync(ct);
            foreach (var r in res.Where(r => r.Status == ReservationStatus.Released)) pools.Blocked.Add((r.ItemId, r.SubstituteForItemId));
            var active = res.Where(r => r.Status == ReservationStatus.Active).ToList();
            pools.ReservedMineAll = active.Where(r => r.BaselineId == b.Id).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => Math.Max(0m, r.Left)));
            pools.ReservedMinePrimary = active.Where(r => r.BaselineId == b.Id && r.SubstituteForItemId == null).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => Math.Max(0m, r.Left)));
            foreach (var g in active.Where(r => r.BaselineId == b.Id && r.SubstituteForItemId.HasValue && r.Left > 0).GroupBy(r => (For: r.SubstituteForItemId.Value, Sub: r.ItemId)))
            {
                if (!pools.Credits.TryGetValue(g.Key.For, out var list)) pools.Credits[g.Key.For] = list = new List<(int Sub, decimal Qty)>();
                list.Add((g.Key.Sub, g.Sum(r => r.Left)));
            }

            // what open material requests of other orders still have to issue (the requests of this work order are this project's own demand)
            var req = await (from l in Uow.Set<MaterialRequestLine>().AsNoTracking()
                             join r in Uow.Set<MaterialRequest>().AsNoTracking() on l.MaterialRequestId equals r.Id
                             where ids.Contains(l.ItemId) && !r.Deleted && (r.Status == MaterialRequestStatus.Submitted || r.Status == MaterialRequestStatus.Approved)
                                   && (r.WorkOrderId == null || r.WorkOrderId != b.WorkOrderId)
                             select new { l.ItemId, WorkOrderId = r.WorkOrderId ?? 0, Left = l.Quantity - l.IssuedQuantity }).ToListAsync(ct);
            // per other work order: the larger of what it reserved and what it asked for (a kit request is drafted from a reservation; the two are one claim)
            var othersRes = active.Where(r => r.BaselineId != b.Id).GroupBy(r => (r.ItemId, r.WorkOrderId)).ToDictionary(g => g.Key, g => g.Sum(r => Math.Max(0m, r.Left)));
            var othersReq = req.GroupBy(r => (r.ItemId, r.WorkOrderId)).ToDictionary(g => g.Key, g => g.Sum(r => Math.Max(0m, r.Left)));
            foreach (var key in othersRes.Keys.Union(othersReq.Keys).GroupBy(k => k.ItemId))
            {
                decimal held = 0m;
                foreach (var k in key) held += Math.Max(othersRes.TryGetValue(k, out decimal a1) ? a1 : 0m, othersReq.TryGetValue(k, out decimal a2) ? a2 : 0m);
                pools.HeldOthers[key.Key] = held;
            }
            pools.SalesHeld = await Sales.SlReservations.ReservedAsync(Uow, ids, ct);

            // supply is counted for ONE project: what is on this work order, and what is on the contract without a work order (given to the first work order of the contract only).
            // Requests and orders with no contract and no work order are general supply and are not counted for any project.
            List<int> contractOrders = await Uow.Set<PbProjectBaseline>().AsNoTracking().Where(x => !x.Deleted && x.IsCurrent && Uow.Set<WorkOrder>().Any(w => w.Id == x.WorkOrderId && w.ContractId == contractId)).Select(x => x.WorkOrderId).ToListAsync(ct);
            bool leadOrder = contractOrders.Count == 0 || contractOrders.Min() == b.WorkOrderId;

            // open purchase requests (submitted or approved) less what is already on orders
            var prLines = await (from l in Uow.Set<PurchaseRequestLine>().AsNoTracking()
                                 join r in Uow.Set<PurchaseRequest>().AsNoTracking() on l.PurchaseRequestId equals r.Id
                                 where l.ItemId != null && ids.Contains(l.ItemId.Value) && !r.Deleted && (r.Status == PurchaseRequestStatus.Submitted || r.Status == PurchaseRequestStatus.Approved
                                       || (r.Status == PurchaseRequestStatus.Returned && r.SupersededByRequestId == null)) // PB6: a returned request still covers the need until its revision exists
                                       && (r.WorkOrderId == b.WorkOrderId || (r.WorkOrderId == null && r.ContractId == contractId && leadOrder))
                                 select new { LineId = l.Id, ItemId = l.ItemId.Value, l.Quantity }).ToListAsync(ct);
            List<int> prLineIds = prLines.Select(l => l.LineId).ToList();
            // "ordered against the request": every order that is not cancelled; an order that was closed short counts what it received
            var ordered = prLineIds.Count == 0 ? new Dictionary<int, decimal>() : (await (from l in Uow.Set<PurchaseOrderLine>().AsNoTracking()
                                                                                      join p in Uow.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.Id
                                                                                      where l.RequestLineId != null && prLineIds.Contains(l.RequestLineId.Value) && !p.Deleted && p.Status != PurchaseOrderStatus.Cancelled
                                                                                      select new { LineId = l.RequestLineId.Value, p.Status, l.Quantity, l.ReceivedQuantity }).ToListAsync(ct))
                .GroupBy(x => x.LineId).ToDictionary(g => g.Key, g => g.Sum(x => x.Status == PurchaseOrderStatus.Closed ? x.ReceivedQuantity : x.Quantity));
            pools.PrOpen = prLines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => Math.Max(0m, l.Quantity - (ordered.TryGetValue(l.LineId, out decimal o) ? o : 0m))));

            // purchase orders on their way (draft, approved, sent, partly received) and still to arrive
            PurchaseOrderStatus[] open = ArrivingStatuses;
            var po = await (from l in Uow.Set<PurchaseOrderLine>().AsNoTracking()
                            join p in Uow.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.Id
                            where l.ItemId != null && ids.Contains(l.ItemId.Value) && !p.Deleted && open.Contains(p.Status)
                                  && ((l.WorkOrderId ?? p.WorkOrderId) == b.WorkOrderId
                                      || ((l.WorkOrderId ?? p.WorkOrderId) == null && (l.ContractId ?? p.ContractId) == contractId && leadOrder))
                            select new { ItemId = l.ItemId.Value, Left = l.Quantity - l.ReceivedQuantity, Delivery = l.DeliveryDate ?? p.DeliveryDate, p.OrderDate }).ToListAsync(ct);
            foreach (var row in po.Where(x => x.Left > 0))
            {
                // no delivery date on the order: the order date plus the lead time of the item
                DateTime arrival = row.Delivery ?? row.OrderDate.AddDays(items.TryGetValue(row.ItemId, out ItemFacts f) ? s.LeadDays(f.GroupCode) : s.LeadDefaultDays);
                if (!pools.Po.TryGetValue(row.ItemId, out List<PoArrival> list)) pools.Po[row.ItemId] = list = new List<PoArrival>();
                list.Add(new PoArrival { Date = arrival.Date, Quantity = row.Left });
            }

            // issued to the work order: confirmed issues (a send to a contractor included) less returns from production and from the contractor
            var issued = await (from dl in Uow.Set<InventoryDocumentLine>().AsNoTracking()
                                join d in Uow.Set<InventoryDocument>().AsNoTracking() on dl.DocumentId equals d.Id
                                where d.WorkOrderId == b.WorkOrderId && !d.Deleted && d.Status == InventoryDocumentStatus.Confirmed && ids.Contains(dl.ItemId)
                                      && (d.Type == InventoryDocumentType.Issue || d.Type == InventoryDocumentType.SubcontractIssue || d.Type == InventoryDocumentType.ProductionReturn || d.Type == InventoryDocumentType.SubcontractReturn)
                                select new { dl.ItemId, d.Type, dl.Quantity }).ToListAsync(ct);
            pools.Issued = issued.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => Math.Max(0m, g.Sum(x => x.Type == InventoryDocumentType.Issue || x.Type == InventoryDocumentType.SubcontractIssue ? x.Quantity : -x.Quantity)));

            // PB5 (R1): stock held in the warehouses of this project (project, project-consumable and point-of-use) is already with the project: it counts as issued, not as free company stock
            var inProject = await (from sb in Uow.Set<StockBalance>().AsNoTracking()
                              join w in Uow.Set<Warehouse>().AsNoTracking() on sb.WarehouseId equals w.Id
                              where ids.Contains(sb.ItemId) && !w.Deleted && w.IsActive && w.WorkOrderId == b.WorkOrderId && sb.Quantity > 0 && (sb.Lot == null || !sb.Lot.IsCustomerProperty)
                              select new { sb.ItemId, sb.Quantity }).ToListAsync(ct);
            // H3: a transfer into a warehouse of this work order uses up the reservation of the work order (ProjectReservationRules.ApplyIssueAsync), so the same pieces are never counted as reserved and as issued
            foreach (var h in inProject.GroupBy(x => x.ItemId))
                pools.Issued[h.Key] = (pools.Issued.TryGetValue(h.Key, out decimal already) ? already : 0m) + h.Sum(x => x.Quantity);
            return pools;
        }

        /// <summary>The requirement per item: a bought part is the item itself, a made part is its raw material; assemblies are not ordered, their parts are.</summary>
        private async Task<(List<Req> Rows, int CustomerSupplied)> ExplodeAsync(PbProjectBaseline b, CancellationToken ct)
        {
            // the whole tree is read (removed lines included): the quantity of a part is the product of its own and its ancestors' quantities, and a removed assembly cuts everything below it
            List<PbProjectBomLine> all = await Uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => l.BaselineId == b.Id && !l.Deleted && !l.IsHeading).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToListAsync(ct);
            List<PbProjectBomLine> lines = all.Where(l => !l.IsRemoved).ToList();
            List<int> srcIds = all.Where(l => l.SourceLineId.HasValue).Select(l => l.SourceLineId.Value).Distinct().ToList();
            Dictionary<int, PbBomLine> src = srcIds.Count == 0 ? new Dictionary<int, PbBomLine>() : await Uow.Set<PbBomLine>().AsNoTracking().Where(l => srcIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
            var parentIds = new HashSet<int>(all.Where(l => l.ParentLineId.HasValue).Select(l => l.ParentLineId.Value));
            bool IsAssembly(PbProjectBomLine l) => parentIds.Contains(l.Id) || (src.TryGetValue(l.SourceLineId ?? 0, out PbBomLine x) && x.ChildBomId.HasValue);
            // the scrap of an assembly is carried by its parts as well
            Dictionary<int, decimal> totals = PbBomTree.Totals(all, Math.Max(1, b.Count), l => IsAssembly(l) && src.TryGetValue(l.SourceLineId ?? 0, out PbBomLine x) && x.ScrapFactor.HasValue ? x.ScrapFactor.Value : 0m);

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
            // PB5: the units-of-measure conversions (item-specific first, then general)
            List<UnitConversionRules.Row> conversions = await UnitConversionRules.LoadAsync(Uow, ct);

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
                // an assembly (it has parts below it) is built from its parts, which are listed after it in the project copy
                if (IsAssembly(l)) continue;
                if (l.MakeOrBuy == PbMakeOrBuy.CustomerSupplied) { customerSupplied++; continue; }
                decimal qty = totals.TryGetValue(l.Id, out decimal tq) ? tq : 0m;
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
                    string itemUnit = items[itemId.Value].Unit;
                    bool mismatch = BalanceRules.UnitsDiffer(l.Unit, itemUnit);
                    string convertedNote = null;
                    if (mismatch)
                    {
                        decimal? cf = UnitConversionRules.Factor(conversions, itemId, l.Unit, itemUnit);
                        if (cf.HasValue)
                        {
                            gross = BalanceRules.Round(gross * cf.Value);
                            scrap = BalanceRules.Round(scrap * cf.Value);
                            mismatch = false;
                            convertedNote = "تبدیل واحد: " + l.Unit + " به " + itemUnit + " با ضریب " + cf.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                    rows.Add(new Req { Kind = BalanceRequirementKind.Purchased, ItemId = itemId, PartNumber = pn, PartName = l.PartName, Unit = itemUnit ?? l.Unit, MakeOrBuy = (int)l.MakeOrBuy, Gross = gross, Scrap = scrap, AssemblyPartNumber = l.AssemblyPartNumber,
                        UnitMismatch = mismatch, Note = mismatch ? "واحد BOM (" + l.Unit + ") با واحد کالا (" + itemUnit + ") فرق دارد و تبدیلی تعریف نشده است؛ مقدار تبدیل نشد، بررسی شود." : convertedNote });
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
                if (seen.TryGetValue(key, out Req first)) { first.UnitMismatch |= r.UnitMismatch; first.Note = first.Note ?? r.Note; first.Gross = BalanceRules.Round(first.Gross + r.Gross); first.Scrap = BalanceRules.Round(first.Scrap + r.Scrap); }
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
        private Task<RunOutcome> ExecuteAsync(int baselineId, string trigger, BalanceSettings s, CancellationToken ct) =>
            // the run, its lines and its reservations are saved in ONE transaction: a run that fails half way leaves nothing behind (the test database has no transactions and runs the work directly)
            InTransactionAsync(() => ExecuteCoreAsync(baselineId, trigger, s, ct), o => o != null, ct);

        private async Task<RunOutcome> ExecuteCoreAsync(int baselineId, string trigger, BalanceSettings s, CancellationToken ct)
        {
            PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().FirstAsync(x => x.Id == baselineId, ct);
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

            // ONE free pool per item shared by every use: the primary demand of all items is covered first, then the substitutes cover the remaining shortages from what is still free.
            // Stock of a substitute that is already reserved for this project counts for the item it replaces (divided by the ratio) and is not free for anybody else.
            var freeLeft = new Dictionary<int, decimal>();
            decimal FreeLeft(int id) { if (!freeLeft.TryGetValue(id, out decimal v)) freeLeft[id] = v = pools.Free(id); return v; }
            var lines = new List<ProjectMaterialBalanceLine>();
            var built = new List<(ProjectMaterialBalanceLine Line, Req R)>();
            var newReservations = new Dictionary<int, (decimal Qty, Req First)>();
            var substituteReservations = new Dictionary<(int Sub, int For), (decimal Qty, Req First)>();
            int sort = 0;
            foreach (var group in rows.Where(r => r.ItemId.HasValue).GroupBy(r => r.ItemId.Value).OrderBy(g => g.Key))
            {
                int id = group.Key;
                List<Req> ordered = group.OrderBy(r => r.UseBy ?? DateTime.MaxValue).ThenBy(r => r.PartNumber, StringComparer.Ordinal).ToList();
                var demand = ordered.Select(r => new DemandRow { Required = BalanceRules.RoundUnit(r.Gross + r.Scrap, r.Unit), UseBy = r.UseBy }).ToList();
                // substitute stock reserved for this project, in units of this item
                var caps = new List<(int Sub, decimal Ratio, decimal Cap)>();
                if (pools.Credits.TryGetValue(id, out var credits))
                    foreach (var c in credits)
                    {
                        ItemSubstitute sub = subs.FirstOrDefault(x => x.ItemId == id && x.SubstituteItemId == c.Sub);
                        if (sub != null && sub.Ratio > 0) caps.Add((c.Sub, sub.Ratio, BalanceRules.Round(c.Qty / sub.Ratio)));
                    }
                decimal creditTotal = caps.Sum(c => c.Cap);
                bool blocked = pools.Blocked.Contains((id, (int?)null));
                decimal freeBefore = FreeLeft(id);
                List<RowAllocation> alloc = BalanceRules.Allocate(demand, pools.Get(pools.Issued, id), pools.Get(pools.ReservedMinePrimary, id), creditTotal, freeBefore, s.ReserveOnRun && !blocked, pools.Get(pools.PrOpen, id),
                    pools.Po.TryGetValue(id, out List<PoArrival> po) ? po : null);
                freeLeft[id] = Math.Max(0m, freeBefore - alloc.Sum(x => x.FromFree));
                decimal onHand = pools.Get(pools.OnHand, id), available = pools.Free(id);
                for (int i = 0; i < ordered.Count; i++)
                {
                    Req r = ordered[i];
                    RowAllocation a = alloc[i];
                    var line = new ProjectMaterialBalanceLine
                    {
                        Kind = r.Kind, ItemId = id, PartNumber = r.PartNumber, PartName = Cut(r.PartName, 300), Unit = r.Unit, MakeOrBuy = r.MakeOrBuy, WbsNodeId = r.NodeId, ActivityId = r.ActivityId,
                        NeedDate = r.Need, UseByDate = r.UseBy, LeadTimeDays = r.Lead, TotalFloat = r.Float, Priority = r.Priority, UnitMismatch = r.UnitMismatch,
                        GrossQty = r.Gross, ScrapQty = BalanceRules.Round(demand[i].Required - r.Gross), RequiredQty = demand[i].Required, IssuedQty = BalanceRules.Round(a.Issued), ReservedQty = BalanceRules.Round(a.Reserved), ReservedNewQty = BalanceRules.Round(a.ReservedNew),
                        OnHandQty = onHand, AvailableQty = available, OnOrderPrQty = BalanceRules.Round(a.Pr), OnOrderPoQty = BalanceRules.Round(a.Po), ShortQty = a.Short, Note = r.Note,
                    };
                    if (a.ReservedNew > 0)
                    {
                        newReservations.TryGetValue(id, out var cur);
                        newReservations[id] = (cur.Qty + a.ReservedNew, cur.First ?? r);
                    }
                    // the part of the demand covered by substitute stock that is already reserved for the project
                    decimal creditLeft = a.Credit;
                    for (int k = 0; k < caps.Count && creditLeft > 0; k++)
                    {
                        decimal take = Math.Min(creditLeft, caps[k].Cap);
                        if (take <= 0) continue;
                        caps[k] = (caps[k].Sub, caps[k].Ratio, caps[k].Cap - take);
                        creditLeft -= take;
                        line.SubstituteQty = BalanceRules.Round(line.SubstituteQty + take * caps[k].Ratio);
                        line.SubstituteItemId = line.SubstituteItemId ?? caps[k].Sub;
                        string code = subItems.TryGetValue(caps[k].Sub, out ItemFacts cf) ? cf.Code : "#" + caps[k].Sub;
                        line.Note = (line.Note == null ? "" : line.Note + " | ") + "جایگزین رزروشده " + code + ": " + BalanceRules.Num(BalanceRules.Round(take * caps[k].Ratio)) + " به‌جای " + BalanceRules.Num(take);
                    }
                    built.Add((line, r));
                    lines.Add(line);
                }
            }

            // the shortages that remain: an approved substitute with free stock (what the primary demand of every item left) covers what it can
            foreach (var (line, r) in built)
            {
                if (line.ShortQty <= 0) continue;
                int id = line.ItemId.Value;
                foreach (ItemSubstitute sub in subs.Where(x => x.ItemId == id))
                {
                    if (line.ShortQty <= 0) break;
                    decimal subFree = FreeLeft(sub.SubstituteItemId);
                    (decimal covered, decimal units) = BalanceRules.Substitute(line.ShortQty, sub.Ratio, subFree);
                    if (BalanceRules.IsCountUnit(line.Unit)) { covered = Math.Floor(covered); units = Math.Min(BalanceRules.Round(covered * sub.Ratio), subFree); }
                    if (covered <= 0) continue;
                    freeLeft[sub.SubstituteItemId] = Math.Max(0m, subFree - units);
                    line.ShortQty = BalanceRules.Round(line.ShortQty - covered);
                    line.SubstituteQty = BalanceRules.Round(line.SubstituteQty + units);
                    line.SubstituteItemId = line.SubstituteItemId ?? sub.SubstituteItemId;
                    string code = subItems.TryGetValue(sub.SubstituteItemId, out ItemFacts sf) ? sf.Code : "#" + sub.SubstituteItemId;
                    line.Note = (line.Note == null ? "" : line.Note + " | ") + "جایگزین تاییدشده " + code + ": " + BalanceRules.Num(units) + " " + (subItems.TryGetValue(sub.SubstituteItemId, out ItemFacts sf2) ? sf2.Unit : "") + " به‌جای " + BalanceRules.Num(covered) + " (نسبت " + BalanceRules.Num(sub.Ratio) + ")";
                    if (s.ReserveOnRun && !pools.Blocked.Contains((sub.SubstituteItemId, (int?)id)))
                    {
                        var key = (sub.SubstituteItemId, id);
                        substituteReservations.TryGetValue(key, out var cs);
                        substituteReservations[key] = (cs.Qty + units, cs.First ?? r);
                    }
                }
            }
            // rows with no item code are listed and flagged
            foreach (Req r in rows.Where(r => !r.ItemId.HasValue))
                lines.Add(new ProjectMaterialBalanceLine
                {
                    Kind = r.Kind, PartNumber = r.PartNumber, PartName = Cut(r.PartName, 300), Unit = r.Unit, MakeOrBuy = r.MakeOrBuy, WbsNodeId = r.NodeId, ActivityId = r.ActivityId, NeedDate = r.Need, UseByDate = r.UseBy,
                    LeadTimeDays = r.Lead, TotalFloat = r.Float, Priority = r.Priority, GrossQty = r.Gross, ScrapQty = r.Scrap, RequiredQty = BalanceRules.Round(r.Gross + r.Scrap), Note = r.Note, UnitMismatch = r.UnitMismatch,
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

        /// <summary>True when the exception is the database refusing a duplicate (a concurrent run took the same run number or created the same reservation) or a concurrent change.</summary>
        private static bool IsConcurrent(Exception ex) => ex is DbUpdateException;

        private void Reset() { if (Uow is DbContext db) db.ChangeTracker.Clear(); }

        public async Task<ResponseDto<BalanceRunBatchDto>> RunAsync(BalanceRunRequestDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.Planning)) return ResponseDtoHelper.BadRequest<BalanceRunBatchDto>(BalanceMessages.NotAllowed);
            if (dto == null || (!dto.WorkOrderId.HasValue && !dto.ContractId.HasValue)) return ResponseDtoHelper.BadRequest<BalanceRunBatchDto>(BalanceMessages.InvalidData);
            IQueryable<PbProjectBaseline> q = Uow.Set<PbProjectBaseline>().AsNoTracking().Where(x => !x.Deleted && x.IsCurrent);
            if (dto.WorkOrderId.HasValue) q = q.Where(x => x.WorkOrderId == dto.WorkOrderId.Value);
            else
            {
                int cid = dto.ContractId.Value;
                q = q.Where(x => Uow.Set<WorkOrder>().Any(w => w.Id == x.WorkOrderId && w.ContractId == cid));
            }
            List<int> baselineIds = await q.OrderBy(x => x.WorkOrderId).Select(x => x.Id).ToListAsync(ct);
            if (baselineIds.Count == 0) return ResponseDtoHelper.BadRequest<BalanceRunBatchDto>(BalanceMessages.NoBaseline);
            BalanceSettings s = await BalanceSettingKeys.LoadAsync(Uow, ct);
            var batch = new BalanceRunBatchDto();
            foreach (int baselineId in baselineIds)
            {
                try
                {
                    RunOutcome outcome = await ExecuteAsync(baselineId, "Manual", s, ct);
                    batch.Runs.Add(await RunDtoAsync(outcome.Run.Id, true, ct));
                    if (dto.CreateDrafts)
                    {
                        ResponseDto<BalanceDraftsDto> drafts = await DraftsCoreAsync(outcome.Run.Id, s, ct);
                        if (drafts.Success) batch.Drafts.Add(drafts.Data); else batch.Notes.Add(drafts.Message);
                    }
                }
                catch (Exception ex) when (IsConcurrent(ex))
                {
                    // another run of the same project is being saved at this moment: nothing of this one was stored
                    Reset();
                    return ResponseDtoHelper.Conflict<BalanceRunBatchDto>(BalanceMessages.RunBusy);
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
                int baselineId = await Uow.Set<PbProjectBaseline>().AsNoTracking().Where(x => x.WorkOrderId == workOrderId && !x.Deleted && x.IsCurrent).Select(x => x.Id).FirstOrDefaultAsync(ct);
                if (baselineId == 0) return;
                RunOutcome outcome = await ExecuteAsync(baselineId, trigger, s, ct);
                if (s.AutoDraftRequests && outcome.Run != null) await DraftsCoreAsync(outcome.Run.Id, s, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // the balance is a help: whatever made it fail (a concurrent run included) must not undo the instantiation or the deviation that triggered it
                Reset();
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
                    RunOutcome outcome = await ExecuteAsync(id, "Nightly", s, ct);
                    if (outcome.Stored) stored++;
                    if (s.AutoDraftRequests && outcome.Stored) await DraftsCoreAsync(outcome.Run.Id, s, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { Reset(); }
            }
            return stored;
        }
    }
}
