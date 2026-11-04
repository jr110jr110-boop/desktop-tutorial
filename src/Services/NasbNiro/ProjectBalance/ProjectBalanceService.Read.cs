using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectBalance;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace Services.NasbNiro.ProjectBalance
{
    public sealed partial class ProjectBalanceService
    {
        // ------------------------------------------------------------------------------------------------ reads

        public async Task<ResponseDto<List<BalanceProjectDto>>> GetProjectsAsync(CancellationToken ct)
        {
            var baselines = await (from b in Uow.Set<PbProjectBaseline>().AsNoTracking()
                                   join w in Uow.Set<WorkOrder>().AsNoTracking() on b.WorkOrderId equals w.Id
                                   where !b.Deleted && b.IsCurrent
                                   orderby w.ContractId, w.Id
                                   select new BalanceProjectDto
                                   {
                                       WorkOrderId = w.Id, WorkOrderCode = w.Code, WorkOrderTitle = w.Title, ContractId = w.ContractId, ContractCode = w.Contract.Code, BaselineId = b.Id,
                                       BomCode = b.SourceBomCode, BomRevision = b.SourceBomRevision, Count = b.Count,
                                   }).ToListAsync(ct);
            List<int> ids = baselines.Select(b => b.BaselineId).ToList();
            var runs = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(r => !r.Deleted && ids.Contains(r.BaselineId)).Select(r => new { r.Id, r.BaselineId, r.RunNumber, r.RunAt, r.LineCount, r.ShortLineCount }).ToListAsync(ct);
            var reservations = await Uow.Set<ProjectReservation>().AsNoTracking().Where(r => !r.Deleted && r.Status == ReservationStatus.Active && ids.Contains(r.BaselineId)).GroupBy(r => r.BaselineId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
            var drafts = await Uow.Set<PurchaseRequest>().AsNoTracking().Where(r => !r.Deleted && r.Status == PurchaseRequestStatus.Draft && r.ProjectBaselineId != null && ids.Contains(r.ProjectBaselineId.Value))
                .GroupBy(r => r.ProjectBaselineId.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
            foreach (BalanceProjectDto p in baselines)
            {
                var latest = runs.Where(r => r.BaselineId == p.BaselineId).OrderByDescending(r => r.RunNumber).FirstOrDefault();
                if (latest != null) { p.LatestRunId = latest.Id; p.LatestRunNumber = latest.RunNumber; p.LatestRunAt = latest.RunAt; p.LineCount = latest.LineCount; p.ShortLineCount = latest.ShortLineCount; }
                p.ReservationCount = reservations.FirstOrDefault(r => r.Id == p.BaselineId)?.N ?? 0;
                p.DraftRequestCount = drafts.FirstOrDefault(r => r.Id == p.BaselineId)?.N ?? 0;
            }
            return ResponseDtoHelper.Ok(baselines);
        }

        public async Task<ResponseDto<List<BalanceRunListItemDto>>> GetRunsAsync(int workOrderId, CancellationToken ct)
        {
            List<BalanceRunListItemDto> list = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(r => !r.Deleted && r.WorkOrderId == workOrderId).OrderByDescending(r => r.RunNumber)
                .Select(r => new BalanceRunListItemDto
                {
                    Id = r.Id, RunNumber = r.RunNumber, RunAt = r.RunAt, RunByUserId = r.RunByUserId, Trigger = r.Trigger, BomCode = r.BomCode, BomRevision = r.BomRevision,
                    LineCount = r.LineCount, ShortLineCount = r.ShortLineCount, NotCodedCount = r.NotCodedCount,
                }).Take(200).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(Uow, list.Select(r => (int?)r.RunByUserId).Where(x => x > 0), ct);
            foreach (BalanceRunListItemDto r in list) r.RunByName = r.RunByUserId == 0 ? "کار خودکار" : names.Name(r.RunByUserId);
            return ResponseDtoHelper.Ok(list);
        }

        private async Task<BalanceRunDto> RunDtoAsync(int id, bool withLines, CancellationToken ct)
        {
            ProjectMaterialBalance r = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (r == null) return null;
            string woCode = await Uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == r.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            int latest = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(x => x.BaselineId == r.BaselineId && !x.Deleted).MaxAsync(x => x.RunNumber, ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(Uow, new int?[] { r.RunByUserId > 0 ? r.RunByUserId : (int?)null }, ct);
            var dto = new BalanceRunDto
            {
                Id = r.Id, RunNumber = r.RunNumber, ContractId = r.ContractId, WorkOrderId = r.WorkOrderId, WorkOrderCode = woCode, BaselineId = r.BaselineId, BomHeaderId = r.BomHeaderId, BomCode = r.BomCode,
                BomRevision = r.BomRevision, RunAt = r.RunAt, RunByUserId = r.RunByUserId, RunByName = r.RunByUserId == 0 ? "کار خودکار" : names.Name(r.RunByUserId), Trigger = r.Trigger, SettingsJson = r.SettingsJson,
                PreviousRunId = r.PreviousRunId, LineCount = r.LineCount, ShortLineCount = r.ShortLineCount, NotCodedCount = r.NotCodedCount, Note = r.Note, IsLatest = r.RunNumber == latest,
            };
            if (withLines) dto.Lines = await LineDtosAsync(await Uow.Set<ProjectMaterialBalanceLine>().AsNoTracking().Where(l => l.BalanceId == id && !l.Deleted).OrderBy(l => l.SortOrder).ToListAsync(ct), ct);
            return dto;
        }

        private async Task<List<BalanceLineDto>> LineDtosAsync(List<ProjectMaterialBalanceLine> lines, CancellationToken ct)
        {
            List<int> itemIds = lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value).Concat(lines.Where(l => l.SubstituteItemId.HasValue).Select(l => l.SubstituteItemId.Value)).Distinct().ToList();
            Dictionary<int, ItemFacts> items = await LoadItemsAsync(itemIds, ct);
            List<int> nodeIds = lines.Where(l => l.WbsNodeId.HasValue).Select(l => l.WbsNodeId.Value).Distinct().ToList();
            Dictionary<int, (string Code, string Title)> nodes = nodeIds.Count == 0 ? new Dictionary<int, (string, string)>() : (await Uow.Set<PcWbsNode>().AsNoTracking().Where(n => nodeIds.Contains(n.Id)).Select(n => new { n.Id, n.Code, n.Title }).ToListAsync(ct)).ToDictionary(n => n.Id, n => (n.Code, n.Title));
            List<int> actIds = lines.Where(l => l.ActivityId.HasValue).Select(l => l.ActivityId.Value).Distinct().ToList();
            Dictionary<int, string> acts = actIds.Count == 0 ? new Dictionary<int, string>() : (await Uow.Set<PcActivity>().AsNoTracking().Where(a => actIds.Contains(a.Id)).Select(a => new { a.Id, a.Code }).ToListAsync(ct)).ToDictionary(a => a.Id, a => a.Code);
            List<int> prIds = lines.Where(l => l.PurchaseRequestId.HasValue).Select(l => l.PurchaseRequestId.Value).Distinct().ToList();
            Dictionary<int, string> prs = prIds.Count == 0 ? new Dictionary<int, string>() : (await Uow.Set<PurchaseRequest>().AsNoTracking().Where(p => prIds.Contains(p.Id)).Select(p => new { p.Id, p.Number }).ToListAsync(ct)).ToDictionary(p => p.Id, p => p.Number);
            return lines.Select(l => new BalanceLineDto
            {
                Id = l.Id, SortOrder = l.SortOrder, Kind = l.Kind, ItemId = l.ItemId, ItemCode = l.ItemId.HasValue && items.TryGetValue(l.ItemId.Value, out ItemFacts f) ? f.Code : null,
                ItemTitle = l.ItemId.HasValue && items.TryGetValue(l.ItemId.Value, out ItemFacts f2) ? f2.Title : null, PartNumber = l.PartNumber, PartName = l.PartName, Unit = l.Unit, MakeOrBuy = l.MakeOrBuy,
                WbsNodeId = l.WbsNodeId, WbsNodeCode = l.WbsNodeId.HasValue && nodes.TryGetValue(l.WbsNodeId.Value, out var n) ? n.Code : null, WbsNodeTitle = l.WbsNodeId.HasValue && nodes.TryGetValue(l.WbsNodeId.Value, out var n2) ? n2.Title : null,
                ActivityId = l.ActivityId, ActivityCode = l.ActivityId.HasValue && acts.TryGetValue(l.ActivityId.Value, out string ac) ? ac : null, NeedDate = l.NeedDate, UseByDate = l.UseByDate, LeadTimeDays = l.LeadTimeDays,
                TotalFloat = l.TotalFloat, Priority = l.Priority, GrossQty = l.GrossQty, ScrapQty = l.ScrapQty, RequiredQty = l.RequiredQty, IssuedQty = l.IssuedQty, ReservedQty = l.ReservedQty, ReservedNewQty = l.ReservedNewQty,
                OnHandQty = l.OnHandQty, AvailableQty = l.AvailableQty, OnOrderPrQty = l.OnOrderPrQty, OnOrderPoQty = l.OnOrderPoQty, SubstituteQty = l.SubstituteQty, SubstituteItemId = l.SubstituteItemId,
                SubstituteItemCode = l.SubstituteItemId.HasValue && items.TryGetValue(l.SubstituteItemId.Value, out ItemFacts sf) ? sf.Code : null, ShortQty = l.ShortQty, PurchaseRequestId = l.PurchaseRequestId,
                PurchaseRequestNumber = l.PurchaseRequestId.HasValue && prs.TryGetValue(l.PurchaseRequestId.Value, out string pn) ? pn : null, PurchaseRequestLineId = l.PurchaseRequestLineId, Note = l.Note,
            }).ToList();
        }

        public async Task<ResponseDto<BalanceRunDto>> GetRunAsync(int id, CancellationToken ct)
        {
            BalanceRunDto dto = await RunDtoAsync(id, true, ct);
            return dto == null ? ResponseDtoHelper.NotFound<BalanceRunDto>(BalanceMessages.NotFound) : ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<BalanceRunDto>> GetLatestAsync(int workOrderId, CancellationToken ct)
        {
            int? id = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(r => r.WorkOrderId == workOrderId && !r.Deleted).OrderByDescending(r => r.RunNumber).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
            if (id == null) return ResponseDtoHelper.NotFound<BalanceRunDto>("برای این پروژه هنوز بالانس اجرا نشده است.");
            return ResponseDtoHelper.Ok(await RunDtoAsync(id.Value, true, ct));
        }

        public async Task<ResponseDto<BalanceDiffDto>> GetDiffAsync(int runId, int? againstRunId, CancellationToken ct)
        {
            ProjectMaterialBalance to = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId && !r.Deleted, ct);
            if (to == null) return ResponseDtoHelper.NotFound<BalanceDiffDto>(BalanceMessages.NotFound);
            int? fromId = againstRunId ?? to.PreviousRunId;
            if (fromId == null) return ResponseDtoHelper.BadRequest<BalanceDiffDto>("این اولین اجرا است و اجرای قبلی برای مقایسه ندارد.");
            ProjectMaterialBalance from = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == fromId.Value && !r.Deleted, ct);
            if (from == null || from.BaselineId != to.BaselineId) return ResponseDtoHelper.BadRequest<BalanceDiffDto>("اجرای مقایسه برای همین پروژه پیدا نشد.");
            List<ProjectMaterialBalanceLine> a = await Uow.Set<ProjectMaterialBalanceLine>().AsNoTracking().Where(l => l.BalanceId == from.Id && !l.Deleted).ToListAsync(ct);
            List<ProjectMaterialBalanceLine> b = await Uow.Set<ProjectMaterialBalanceLine>().AsNoTracking().Where(l => l.BalanceId == to.Id && !l.Deleted).ToListAsync(ct);
            Dictionary<int, ItemFacts> items = await LoadItemsAsync(a.Concat(b).Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value), ct);
            string Key(ProjectMaterialBalanceLine l) => (l.ItemId?.ToString() ?? "-") + "|" + l.PartNumber + "|" + l.Kind;
            Dictionary<string, ProjectMaterialBalanceLine> oldRows = a.GroupBy(Key).ToDictionary(g => g.Key, g => Merge(g.ToList()));
            Dictionary<string, ProjectMaterialBalanceLine> newRows = b.GroupBy(Key).ToDictionary(g => g.Key, g => Merge(g.ToList()));
            var dto = new BalanceDiffDto { FromRunId = from.Id, ToRunId = to.Id, FromRunNumber = from.RunNumber, ToRunNumber = to.RunNumber };
            string Code(ProjectMaterialBalanceLine l) => l.ItemId.HasValue && items.TryGetValue(l.ItemId.Value, out ItemFacts f) ? f.Code : null;
            foreach (string key in oldRows.Keys.Union(newRows.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                oldRows.TryGetValue(key, out ProjectMaterialBalanceLine o);
                newRows.TryGetValue(key, out ProjectMaterialBalanceLine n);
                string change = o == null ? "Added" : n == null ? "Removed" : (o.RequiredQty != n.RequiredQty || o.ShortQty != n.ShortQty || o.ReservedQty != n.ReservedQty) ? "Changed" : null;
                if (change == null) continue;
                ProjectMaterialBalanceLine any = n ?? o;
                dto.Rows.Add(new BalanceDiffRowDto
                {
                    Change = change, ItemId = any.ItemId, ItemCode = Code(any), PartNumber = any.PartNumber, PartName = any.PartName, Unit = any.Unit,
                    OldRequired = o?.RequiredQty, NewRequired = n?.RequiredQty, OldShort = o?.ShortQty, NewShort = n?.ShortQty, OldReserved = o?.ReservedQty, NewReserved = n?.ReservedQty,
                });
                if (change == "Added") dto.Added++; else if (change == "Removed") dto.Removed++; else dto.Changed++;
            }
            return ResponseDtoHelper.Ok(dto);
        }

        private static ProjectMaterialBalanceLine Merge(List<ProjectMaterialBalanceLine> rows)
        {
            if (rows.Count == 1) return rows[0];
            ProjectMaterialBalanceLine first = rows[0];
            return new ProjectMaterialBalanceLine
            {
                ItemId = first.ItemId, PartNumber = first.PartNumber, PartName = first.PartName, Unit = first.Unit, Kind = first.Kind,
                RequiredQty = rows.Sum(r => r.RequiredQty), ShortQty = rows.Sum(r => r.ShortQty), ReservedQty = rows.Sum(r => r.ReservedQty),
            };
        }

        public async Task<ResponseDto<NodeMaterialDto>> GetNodeMaterialAsync(int nodeId, CancellationToken ct)
        {
            PcWbsNode node = await Uow.Set<PcWbsNode>().AsNoTracking().FirstOrDefaultAsync(n => n.Id == nodeId && !n.Deleted, ct);
            if (node == null) return ResponseDtoHelper.NotFound<NodeMaterialDto>(BalanceMessages.NotFound);
            // the node and everything under it
            var ids = new HashSet<int> { nodeId };
            var frontier = new List<int> { nodeId };
            while (frontier.Count > 0 && ids.Count < 5000)
            {
                List<int> next = await Uow.Set<PcWbsNode>().AsNoTracking().Where(n => !n.Deleted && n.ParentId != null && frontier.Contains(n.ParentId.Value)).Select(n => n.Id).ToListAsync(ct);
                frontier = next.Where(ids.Add).ToList();
            }
            var dto = new NodeMaterialDto { NodeId = node.Id, NodeCode = node.Code, NodeTitle = node.Title };
            // the latest run of every baseline of the contract
            var all = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == node.ContractId).Select(r => new { r.Id, r.BaselineId, r.RunNumber, r.RunAt }).ToListAsync(ct);
            var runs = all.GroupBy(r => r.BaselineId).Select(g => g.OrderByDescending(r => r.RunNumber).First()).ToList();
            List<int> runIds = runs.Select(r => r.Id).ToList();
            if (runIds.Count == 0) return ResponseDtoHelper.Ok(dto);
            var pick = runs.OrderByDescending(r => r.RunAt).First();
            dto.RunId = pick.Id; dto.RunNumber = pick.RunNumber; dto.RunAt = pick.RunAt;
            List<ProjectMaterialBalanceLine> lines = await Uow.Set<ProjectMaterialBalanceLine>().AsNoTracking().Where(l => runIds.Contains(l.BalanceId) && !l.Deleted && l.WbsNodeId != null && ids.Contains(l.WbsNodeId.Value)).OrderBy(l => l.SortOrder).ToListAsync(ct);
            dto.Lines = await LineDtosAsync(lines, ct);
            dto.LineCount = dto.Lines.Count;
            dto.ShortLineCount = dto.Lines.Count(l => l.ShortQty > 0);
            return ResponseDtoHelper.Ok(dto);
        }

        // ------------------------------------------------------------------------------------------------ settings

        public async Task<ResponseDto<List<BalanceSettingDto>>> GetSettingsAsync(CancellationToken ct)
        {
            Dictionary<string, string> values = await BalanceSettingKeys.LoadRawAsync(Uow, ct);
            return ResponseDtoHelper.Ok(BalanceSettingKeys.Defaults().Select(d => new BalanceSettingDto { Key = d.Key, Value = values[d.Key], Title = d.Title, Source = d.Source }).ToList());
        }

        public async Task<ResponseDto<BalanceSettingDto>> SaveSettingAsync(SaveBalanceSettingDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.Planning)) return ResponseDtoHelper.BadRequest<BalanceSettingDto>(BalanceMessages.NotAllowed);
            if (IsAi) return ResponseDtoHelper.BadRequest<BalanceSettingDto>(BalanceMessages.AiNeverApproves);
            PbSetting def = dto == null ? null : BalanceSettingKeys.Defaults().FirstOrDefault(d => d.Key == dto.Key);
            if (def == null) return ResponseDtoHelper.NotFound<BalanceSettingDto>("تنظیم پیدا نشد.");
            string value = dto.Value?.Trim();
            string error = BalanceSettingKeys.Validate(def.Key, value);
            if (error != null) return ResponseDtoHelper.BadRequest<BalanceSettingDto>(error);
            PbSetting row = await Uow.Set<PbSetting>().FirstOrDefaultAsync(x => x.Key == def.Key, ct);
            if (row == null)
            {
                row = new PbSetting { Key = def.Key, Title = def.Title, Source = def.Source };
                Uow.Set<PbSetting>().Add(row);
            }
            row.Value = value;
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(new BalanceSettingDto { Key = row.Key, Value = row.Value, Title = row.Title, Source = row.Source });
        }
    }
}
