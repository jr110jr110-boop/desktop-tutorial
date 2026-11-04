using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Trace
{
    public sealed partial class TraceCenterService
    {
        // ------------------------------------------------------------------------------------------ small helpers

        private TraceNodeDto Node(TreeCtx c, string kind, string basis, string title, string code = null, string status = null, string tone = null)
        {
            c.NodeCount++;
            return new TraceNodeDto { Key = "n" + c.NodeCount.ToString(CultureInfo.InvariantCulture), Kind = kind, Basis = basis, Title = title, Code = code, Status = status, Tone = tone };
        }

        private static bool Full(TreeCtx c)
        {
            if (c.NodeCount < c.MaxNodes) return false;
            c.Truncated = true;
            return true;
        }

        private static void Fact(TraceNodeDto n, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) n.Facts.Add(new TraceFactDto(label, value));
        }

        private static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private List<TraceFileDto> FilesOf(TreeCtx c, AttachmentOwnerType type, int id) =>
            c.Files.TryGetValue(FileKey(type, id), out List<TraceFileDto> list) ? list.ToList() : new List<TraceFileDto>();

        /// <summary>The technical files of the purchase request behind a receipt: shown on the order, the receipt, the inspection and the NCR, marked as propagated.</summary>
        private static List<TraceFileDto> RequestFilesOf(TreeCtx c, ReceiptRow r)
        {
            var list = new List<TraceFileDto>();
            if (r == null) return list;
            if (r.RequestLineId.HasValue && c.RequestLineFiles.TryGetValue(r.RequestLineId.Value, out List<TraceFileDto> lineFiles)) list.AddRange(lineFiles);
            if (r.PrId.HasValue && c.RequestFiles.TryGetValue(r.PrId.Value, out List<TraceFileDto> prFiles)) list.AddRange(prFiles);
            return list.Select(f => new TraceFileDto
            {
                Id = f.Id, DocumentId = f.DocumentId, Title = f.Title, FileName = f.FileName, KindTitle = f.KindTitle, Revision = f.Revision, OwnerTitle = f.OwnerTitle, OwnerNumber = r.PrNumber, Propagated = true,
            }).ToList();
        }

        private static string ItemCode(TreeCtx c, int itemId) => c.Items.TryGetValue(itemId, out var i) ? i.Code : null;

        // ------------------------------------------------------------------------------------------ product

        private async Task BuildProductAsync(TreeCtx c, TraceTreeDto dto, Resolution r, TraceQuery query, int depth, CancellationToken ct)
        {
            PbProduct product = r.Product;
            var baselines = await _uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent && b.ProductId == product.Id)
                .Select(b => new { b.Id, b.WorkOrderId, b.BomHeaderId, b.SourceBomCode, b.SourceBomRevision, b.Count }).OrderBy(b => b.WorkOrderId).ToListAsync(ct);
            Dictionary<int, WorkOrderInfo> workOrders = await LoadWorkOrdersAsync(baselines.Select(b => b.WorkOrderId).Distinct().ToList(), ct);

            var chosen = query.WorkOrderId.HasValue && query.WorkOrderId.Value > 0 ? baselines.FirstOrDefault(b => b.WorkOrderId == query.WorkOrderId.Value) : null;
            bool asDesigned = query.WorkOrderId == 0;
            if (query.WorkOrderId > 0 && chosen == null)
            {
                dto.Mode = "choices";
                dto.Notes.Add("برای این پروژه نسخه‌ای از BOM این محصول گرفته نشده است.");
            }
            else if (!query.WorkOrderId.HasValue && baselines.Count > 1)
            {
                dto.Mode = "choices";
            }
            else if (!query.WorkOrderId.HasValue && baselines.Count == 1) chosen = baselines[0];
            if (dto.Mode == "choices")
            {
                foreach (var b in baselines)
                {
                    workOrders.TryGetValue(b.WorkOrderId, out WorkOrderInfo wo);
                    dto.Choices.Add(new TraceChoiceDto { Kind = "project", Title = $"پروژه {wo?.Code}", Description = $"{wo?.ContractTitle ?? wo?.Title} — BOM {b.SourceBomCode} نسخه {b.SourceBomRevision}", Q = product.Code, WorkOrderId = b.WorkOrderId });
                }
                dto.Choices.Add(new TraceChoiceDto { Kind = "product", Title = "طبق طرح (BOM منتشرشده بانک)", Description = "بدون ردیابی ساخت پروژه", Q = product.Code, WorkOrderId = 0 });
                return;
            }
            if (chosen == null) asDesigned = true;

            // ----- the structure
            var lines = new List<StructLine>();
            string rootPn = product.Code;
            string bomText = null;
            int? workOrderId = null;
            WorkOrderInfo woInfo = null;
            if (!asDesigned)
            {
                workOrderId = chosen.WorkOrderId;
                workOrders.TryGetValue(chosen.WorkOrderId, out woInfo);
                string headPn = await _uow.Set<PbBomHeader>().AsNoTracking().Where(h => h.Id == chosen.BomHeaderId).Select(h => h.AssemblyPartNumber).FirstOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(headPn)) rootPn = headPn;
                lines = (await _uow.Set<PbProjectBomLine>().AsNoTracking().Where(l => !l.Deleted && l.BaselineId == chosen.Id && !l.IsHeading && !l.IsRemoved).OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                    .Select(l => new StructLine { Assembly = l.AssemblyPartNumber, PartNumber = l.PartNumber, Name = l.PartName, Qty = l.TotalQty, Unit = l.Unit, Material = l.Material, Mob = l.MakeOrBuy, ItemId = l.ItemId, ItemNumber = l.ItemNumber, SortOrder = l.SortOrder })
                    .ToListAsync(ct));
                bomText = $"{chosen.SourceBomCode} نسخه {chosen.SourceBomRevision}";
                dto.Basis = "project";
            }
            else
            {
                dto.Basis = "asDesigned";
                var head = await _uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.Status == PbBomStatus.Released && (h.ProductId == product.Id || h.AssemblyPartNumber == product.Code))
                    .OrderByDescending(h => h.Revision).Select(h => new { h.Id, h.AssemblyPartNumber, h.Code, h.Revision }).FirstOrDefaultAsync(ct);
                if (head == null) dto.Notes.Add("برای این محصول BOM منتشرشده‌ای در بانک نیست.");
                else
                {
                    rootPn = head.AssemblyPartNumber ?? product.Code;
                    bomText = $"{head.Code} نسخه {head.Revision}";
                    var pending = new List<int> { head.Id };
                    var seen = new HashSet<int> { head.Id };
                    var asmOf = new Dictionary<int, string> { [head.Id] = rootPn };
                    for (int level = 0; level < depth && pending.Count > 0; level++)
                    {
                        List<int> ids = pending;
                        var rows = await _uow.Set<PbBomLine>().AsNoTracking().Where(l => !l.Deleted && !l.IsHeading && ids.Contains(l.BomHeaderId)).OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                            .Select(l => new { l.BomHeaderId, l.PartNumber, l.PartName, l.Qty, l.Unit, l.Material, l.MakeOrBuy, l.ItemId, l.ItemNumber, l.SortOrder, l.ChildBomId, l.AlternativeOfLineId }).ToListAsync(ct);
                        foreach (var l in rows.Where(l => l.AlternativeOfLineId == null))
                            lines.Add(new StructLine { Assembly = asmOf[l.BomHeaderId], PartNumber = l.PartNumber, Name = l.PartName, Qty = l.Qty, Unit = l.Unit, Material = l.Material, Mob = l.MakeOrBuy, ItemId = l.ItemId, ItemNumber = l.ItemNumber, SortOrder = l.SortOrder });
                        pending = new List<int>();
                        List<int> childIds = rows.Where(l => l.ChildBomId.HasValue && seen.Add(l.ChildBomId.Value)).Select(l => l.ChildBomId.Value).ToList();
                        if (childIds.Count > 0)
                            foreach (var h in await _uow.Set<PbBomHeader>().AsNoTracking().Where(h => childIds.Contains(h.Id)).Select(h => new { h.Id, h.AssemblyPartNumber }).ToListAsync(ct))
                            {
                                asmOf[h.Id] = h.AssemblyPartNumber;
                                pending.Add(h.Id);
                            }
                    }
                }
            }
            foreach (StructLine l in lines) l.Assembly = string.IsNullOrWhiteSpace(l.Assembly) ? rootPn : l.Assembly;

            // ----- the reachable lines inside the depth and the part cap
            var byAsm = lines.GroupBy(l => l.Assembly).ToDictionary(g => g.Key, g => g.OrderBy(l => l.SortOrder).ThenBy(l => l.ItemNumber).ToList());
            var partNumbers = new List<string> { rootPn };
            bool Walk(string asm, int level, HashSet<string> path, Action<StructLine, int> visit)
            {
                bool cut = false;
                if (!byAsm.TryGetValue(asm, out List<StructLine> children)) return false;
                foreach (StructLine l in children)
                {
                    visit(l, level);
                    if (byAsm.ContainsKey(l.PartNumber) && !path.Contains(l.PartNumber))
                    {
                        if (level + 1 >= depth) { cut = true; continue; }
                        path.Add(l.PartNumber);
                        cut |= Walk(l.PartNumber, level + 1, path, visit);
                        path.Remove(l.PartNumber);
                    }
                }
                return cut;
            }
            bool depthCut = Walk(rootPn, 0, new HashSet<string> { rootPn }, (l, lv) => { if (partNumbers.Count < MaxParts && !partNumbers.Contains(l.PartNumber)) partNumbers.Add(l.PartNumber); });
            if (depthCut) { dto.Truncated = true; dto.Notes.Add($"ساختار BOM عمیق‌تر از {depth} سطح است؛ سطح‌های پایین‌تر نمایش داده نشد."); }
            if (lines.Select(l => l.PartNumber).Distinct().Count() > MaxParts) { dto.Truncated = true; dto.Notes.Add($"بیش از {MaxParts} قطعه در BOM هست؛ فقط {MaxParts} قطعه اول نمایش داده شد."); }

            // ----- everything around the parts (a fixed number of queries)
            await LoadPartScopeAsync(c, partNumbers, workOrderId, asDesigned ? (int?)null : chosen.Id, product.Id, depth, ct);
            await LoadPartDocumentsAsync(c, partNumbers, ct);
            if (c.CapNotes.Count > 0) { dto.Truncated = true; dto.Notes.AddRange(c.CapNotes); }

            // ----- the nodes
            TraceNodeDto root = Node(c, "product", asDesigned ? "asDesigned" : "asBuilt", product.Title, product.Code);
            root.Link = new TraceLinkDto { Type = "item", Number = product.Code };
            Fact(root, "BOM", bomText);
            Fact(root, "پلتفرم", product.Platform);
            if (woInfo != null)
            {
                Fact(root, "پروژه (دستور ساخت)", woInfo.Code);
                Fact(root, "قرارداد", woInfo.ContractTitle ?? woInfo.ContractCode);
                Fact(root, "کارفرما", woInfo.Customer);
                Fact(root, "تعداد محصول", chosen.Count.ToString(CultureInfo.InvariantCulture));
            }
            var cache = new Dictionary<string, List<TraceNodeDto>>();
            root.Children.AddRange(PartDetails(c, rootPn));   // the product itself: its stage items, final-assembly orders, serials and deliveries
            root.Children.AddRange(BuildStructure(c, byAsm, rootPn, 0, depth, new HashSet<string> { rootPn }, cache));
            dto.Root = root;
        }

        private static void NoteCap(TreeCtx c, string note)
        {
            if (!c.CapNotes.Contains(note)) c.CapNotes.Add(note);
        }

        private List<TraceNodeDto> BuildStructure(TreeCtx c, Dictionary<string, List<StructLine>> byAsm, string asm, int level, int depth, HashSet<string> path, Dictionary<string, List<TraceNodeDto>> cache)
        {
            var list = new List<TraceNodeDto>();
            if (!byAsm.TryGetValue(asm, out List<StructLine> children)) return list;
            foreach (StructLine l in children)
            {
                if (Full(c)) break;
                bool isAsm = byAsm.ContainsKey(l.PartNumber) && !path.Contains(l.PartNumber);
                TraceNodeDto n = PartNode(c, l, isAsm ? "assembly" : "part");
                if (isAsm)
                {
                    if (level + 1 >= depth) n.Truncated = true;
                    else
                    {
                        path.Add(l.PartNumber);
                        n.Children.AddRange(BuildStructure(c, byAsm, l.PartNumber, level + 1, depth, path, cache));
                        path.Remove(l.PartNumber);
                    }
                }
                if (!cache.TryGetValue(l.PartNumber, out List<TraceNodeDto> details)) cache[l.PartNumber] = details = PartDetails(c, l.PartNumber);
                n.Children.AddRange(details);
                list.Add(n);
            }
            return list;
        }

        private TraceNodeDto PartNode(TreeCtx c, StructLine l, string kind)
        {
            TraceNodeDto n = Node(c, kind, "asDesigned", l.Name ?? l.PartNumber, l.PartNumber);
            n.Quantity = l.Qty;
            n.Unit = l.Unit;
            n.Link = new TraceLinkDto { Type = "item", Id = l.ItemId, Number = l.PartNumber };
            Fact(n, "ماده", l.Material);
            Fact(n, "ساخت یا خرید", TraceLabels.MakeOrBuy(l.Mob));
            if (l.ItemNumber.HasValue) Fact(n, "ردیف BOM", l.ItemNumber.Value.ToString(CultureInfo.InvariantCulture));
            return n;
        }

        // ------------------------------------------------------------------------------------------ part

        private async Task BuildPartAsync(TreeCtx c, TraceTreeDto dto, Resolution r, CancellationToken ct)
        {
            string pn = r.PartNumber;
            dto.Basis = "asDesigned";
            var line = await _uow.Set<PbBomLine>().AsNoTracking().Where(l => !l.Deleted && !l.IsHeading && l.PartNumber == pn && l.BomHeader.Status == PbBomStatus.Released)
                .OrderByDescending(l => l.BomHeader.Revision).Select(l => new StructLine { PartNumber = l.PartNumber, Name = l.PartName, Qty = l.Qty, Unit = l.Unit, Material = l.Material, Mob = l.MakeOrBuy, ItemId = l.ItemId, ItemNumber = l.ItemNumber })
                .FirstOrDefaultAsync(ct);
            string itemTitle = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber == pn && i.StageCode2 == null).Select(i => i.Title).FirstOrDefaultAsync(ct);
            line ??= new StructLine { PartNumber = pn, Name = itemTitle ?? pn, Qty = 1, Mob = PbMakeOrBuy.Make };
            await LoadPartScopeAsync(c, new List<string> { pn }, null, null, null, MaxTreeDepth, ct);
            await LoadPartDocumentsAsync(c, new List<string> { pn }, ct);
            if (c.CapNotes.Count > 0) { dto.Truncated = true; dto.Notes.AddRange(c.CapNotes); }

            TraceNodeDto root = PartNode(c, line, "part");
            root.Quantity = null;
            var parents = await _uow.Set<PbBomLine>().AsNoTracking().Where(l => !l.Deleted && l.PartNumber == pn && l.BomHeader.Status == PbBomStatus.Released).Select(l => l.BomHeader.AssemblyPartNumber).Distinct().Take(20).ToListAsync(ct);
            Fact(root, "به‌کاررفته در مجموعه‌های", string.Join("، ", parents));
            root.Children.AddRange(PartDetails(c, pn));
            dto.Root = root;
        }

        // ------------------------------------------------------------------------------------------ serial or lot

        private async Task BuildUnitRootAsync(TreeCtx c, TraceTreeDto dto, Resolution r, CancellationToken ct)
        {
            dto.Basis = "asBuilt";
            string key = r.SerialId.HasValue ? SerialKey(r.SerialId.Value) : LotKey(r.LotId.Value);
            c.SeedLots = r.LotId.HasValue ? new List<int> { r.LotId.Value } : new List<int>();
            c.SeedSerials = r.SerialId.HasValue ? new List<int> { r.SerialId.Value } : new List<int>();
            await ExpandDownAsync(c, c.SeedLots, c.SeedSerials, MaxTreeDepth, ct);
            if (!c.Units.ContainsKey(key)) { dto.Mode = "choices"; return; }
            await LoadAroundAsync(c, new List<int>(), null, ct);
            TraceNodeDto root = UnitNode(c, key, c.MaterialDepth + 2, new HashSet<string>(), false, null);
            if (r.SerialId.HasValue)
            {
                // the assemblies it sits in, up to the top (one query per level, at most ten)
                var chain = new List<string>();
                int? parent = await _uow.Set<SerialUnit>().AsNoTracking().Where(u => u.Id == r.SerialId.Value).Select(u => u.ParentSerialUnitId).FirstOrDefaultAsync(ct);
                var seen = new HashSet<int> { r.SerialId.Value };
                while (parent.HasValue && seen.Add(parent.Value) && chain.Count < 10)
                {
                    var p = await _uow.Set<SerialUnit>().AsNoTracking().Where(u => u.Id == parent.Value).Select(u => new { u.SerialNumber, ItemCode = u.Item.Code, u.ParentSerialUnitId }).FirstOrDefaultAsync(ct);
                    if (p == null) break;
                    chain.Add($"{p.ItemCode} / {p.SerialNumber}");
                    parent = p.ParentSerialUnitId;
                }
                Fact(root, "نصب‌شده در", string.Join(" ← ", chain));
            }
            dto.Root = root;
        }

        // ------------------------------------------------------------------------------------------ scope of the parts

        /// <summary>
        /// Loads what is around a set of part numbers: their items and stage items, the OPC steps (the project's routings or the latest approved ones), the production orders and operations of the work order,
        /// the units (lots and serials) they produced or bought, what those were made of, and the documents around all of it.
        /// </summary>
        private async Task LoadPartScopeAsync(TreeCtx c, List<string> partNumbers, int? workOrderId, int? baselineId, int? productId, int depth, CancellationToken ct)
        {
            // items of the parts: the part itself (PartNumber, or a Code equal to it) and every stage item
            var items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && ((i.PartNumber != null && partNumbers.Contains(i.PartNumber)) || partNumbers.Contains(i.Code)))
                .Select(i => new { i.Id, i.Code, i.Title, i.PartNumber, i.StageCode2 }).ToListAsync(ct);
            foreach (var i in items) c.Items[i.Id] = (i.Code, i.Title, i.PartNumber ?? i.Code, i.StageCode2);
            List<int> partItemIds = items.Select(i => i.Id).ToList();
            foreach (var s in await _uow.Set<PbStage>().AsNoTracking().Where(s => !s.Deleted).Select(s => new { s.Code, s.TitleFa }).ToListAsync(ct)) c.Stages[s.Code] = s.TitleFa;

            // OPC steps
            List<int> routingIds;
            if (baselineId.HasValue) routingIds = await _uow.Set<PbProjectRouting>().AsNoTracking().Where(p => !p.Deleted && p.BaselineId == baselineId.Value).Select(p => p.OpcRoutingId).ToListAsync(ct);
            else
            {
                var candidates = await _uow.Set<PbOpcRouting>().AsNoTracking().Where(r => !r.Deleted && r.Status == PbRoutingStatus.Approved && (productId == null || r.ProductId == productId))
                    .Select(r => new { r.Id, r.PartNumber, r.Revision }).ToListAsync(ct);
                routingIds = candidates.GroupBy(r => r.PartNumber).Select(g => g.OrderByDescending(r => r.Revision).First().Id).ToList();
            }
            if (routingIds.Count > 0)
                foreach (var s in await _uow.Set<PbOpcStep>().AsNoTracking().Where(s => !s.Deleted && routingIds.Contains(s.RoutingId) && partNumbers.Contains(s.PartNumber)).OrderBy(s => s.RoutingId).ThenBy(s => s.Seq)
                    .Select(s => new StepInfo
                    {
                        PartNumber = s.PartNumber, Seq = s.Seq, StageCode = s.StageCode2, Title = s.Title, Kind = s.Kind.ToString(), IsQc = s.IsQcPoint, Evidence = s.RequiredEvidence, Outside = s.IsOutside,
                        StageItemId = s.StageItemId, Hours = s.StandardHours, RoutingTitle = s.Routing.Title, Revision = s.Routing.Revision,
                    }).ToListAsync(ct))
                    c.StepsByPart.AddStep(s);

            // orders (the project's, else the latest few) and their operations
            IQueryable<MfProductionOrder> orders = _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.ItemId != null && partItemIds.Contains(o.ItemId.Value));
            // PB4 review M5: without a project the newest few of EACH part (not of all parts together); the window shrinks as the tree grows and a note says when something was left out
            int partCount = Math.Max(1, partItemIds.Count);
            int perPartOrders = Math.Max(3, Math.Min(20, 600 / partCount));
            int perPartUnits = Math.Max(3, Math.Min(MaxLotsPerPart, 800 / partCount));
            if (workOrderId.HasValue) orders = orders.Where(o => o.WorkOrderId == workOrderId.Value);
            else
            {
                var recent = await orders.OrderByDescending(o => o.Id).Select(o => new { o.Id, ItemId = o.ItemId.Value }).Take(perPartOrders * partCount + 1).ToListAsync(ct);
                var keep = recent.GroupBy(o => o.ItemId).SelectMany(g => g.Take(perPartOrders)).Select(o => o.Id).ToList();
                if (keep.Count < recent.Count) NoteCap(c, $"برای هر قطعه فقط {perPartOrders} دستور تولید آخر نشان داده شد.");
                orders = _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => keep.Contains(o.Id));
            }
            await LoadOrdersAsync(c, orders, ct);

            // the units: made by those orders, bought on the project's orders, or (no project) the latest of the items
            var seedLots = new HashSet<int>();
            var seedSerials = new HashSet<int>();
            List<int> orderIds = c.Orders.Keys.ToList();
            List<string> orderNumbers = c.Orders.Values.Select(o => o.Number).ToList();
            if (orderIds.Count > 0)
            {
                foreach (var l in await _uow.Set<GenealogyLink>().AsNoTracking().Where(l => !l.Deleted && orderIds.Contains(l.ProductionOrderId)).Select(l => new { l.OutputLotId, l.OutputSerialUnitId }).Distinct().ToListAsync(ct))
                {
                    if (l.OutputLotId.HasValue) seedLots.Add(l.OutputLotId.Value);
                    if (l.OutputSerialUnitId.HasValue) seedSerials.Add(l.OutputSerialUnitId.Value);
                }
                seedLots.UnionWith(await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && partItemIds.Contains(l.ItemId) && orderNumbers.Contains(l.LotNumber)).Select(l => l.Id).ToListAsync(ct));
            }
            if (workOrderId.HasValue)
            {
                seedSerials.UnionWith(await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.WorkOrderId == workOrderId.Value && partItemIds.Contains(u.ItemId)).Select(u => u.Id).ToListAsync(ct));
                List<int> poIds = await _uow.Set<Entities.Model.NasbNiro.Procurement.PurchaseOrderLine>().AsNoTracking()
                    .Where(l => l.WorkOrderId == workOrderId.Value && l.ItemId != null && partItemIds.Contains(l.ItemId.Value)).Select(l => l.PurchaseOrderId).Distinct().ToListAsync(ct);
                if (poIds.Count > 0)
                {
                    var boughtLots = new HashSet<int>();
                    var boughtSerials = new HashSet<int>();
                    await AddOrderReceiptsAsync(poIds, boughtLots, boughtSerials, ct);
                    foreach (var l in await _uow.Set<InventoryLot>().AsNoTracking().Where(l => boughtLots.Contains(l.Id) && partItemIds.Contains(l.ItemId)).Select(l => l.Id).ToListAsync(ct)) seedLots.Add(l);
                    foreach (var u in await _uow.Set<SerialUnit>().AsNoTracking().Where(u => boughtSerials.Contains(u.Id) && partItemIds.Contains(u.ItemId)).Select(u => u.Id).ToListAsync(ct)) seedSerials.Add(u);
                }
            }
            else
            {
                var recentLots = await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && partItemIds.Contains(l.ItemId)).OrderByDescending(l => l.Id).Select(l => new { l.Id, l.ItemId }).Take(perPartUnits * partCount + 1).ToListAsync(ct);
                var keptLots = recentLots.GroupBy(l => l.ItemId).SelectMany(g => g.Take(perPartUnits)).Select(l => l.Id).ToList();
                var recentSerials = await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && partItemIds.Contains(u.ItemId)).OrderByDescending(u => u.Id).Select(u => new { u.Id, u.ItemId }).Take(perPartUnits * partCount + 1).ToListAsync(ct);
                var keptSerials = recentSerials.GroupBy(u => u.ItemId).SelectMany(g => g.Take(perPartUnits)).Select(u => u.Id).ToList();
                if (keptLots.Count < recentLots.Count || keptSerials.Count < recentSerials.Count) NoteCap(c, $"برای هر قطعه فقط {perPartUnits} بچ و {perPartUnits} سریال آخر نشان داده شد (برای دیدن بقیه، پروژه را انتخاب کنید).");
                seedLots.UnionWith(keptLots);
                seedSerials.UnionWith(keptSerials);
            }
            c.SeedLots = seedLots.ToList();
            c.SeedSerials = seedSerials.ToList();
            await ExpandDownAsync(c, c.SeedLots, c.SeedSerials, c.MaterialDepth, ct);
            await LoadAroundAsync(c, partItemIds, workOrderId, ct);
        }
    }
}
