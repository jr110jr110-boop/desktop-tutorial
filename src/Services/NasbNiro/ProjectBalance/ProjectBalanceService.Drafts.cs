using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Procurement;
using Services.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectBalance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Attachments;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace Services.NasbNiro.ProjectBalance
{
    public sealed partial class ProjectBalanceService
    {
        /// <summary>The title of a link starts with this text only to show it to the reader; whether an attachment IS a link is the flag <see cref="RecordAttachment.LinkedFromAttachmentId"/>, never the title.</summary>
        public const string LinkPrefix = "[پیوند] ";

        private static bool IsLink(RecordAttachment a) => a.LinkedFromAttachmentId.HasValue;

        public async Task<ResponseDto<BalanceDraftsDto>> CreateDraftsAsync(int runId, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.Planning)) return ResponseDtoHelper.BadRequest<BalanceDraftsDto>(BalanceMessages.NotAllowed);
            BalanceSettings s = await BalanceSettingKeys.LoadAsync(Uow, ct);
            return await DraftsCoreAsync(runId, s, ct);
        }

        private static bool InForce(RecordAttachment a) => !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null;

        /// <summary>
        /// The shortage of a run as draft purchase requests, one per group (item group by default). A draft of the same project and group is updated, never duplicated; a request that was already submitted is respected (what it asks for
        /// is counted as "on order" by the next run). The technical documents (the BOM revision, the drawings and specifications of the parts) are linked to the request and its lines, so the next steps see them.
        /// The drafts are approved by planning with the usual gates; nothing here submits.
        /// </summary>
        private async Task<ResponseDto<BalanceDraftsDto>> DraftsCoreAsync(int runId, BalanceSettings s, CancellationToken ct)
        {
            ProjectMaterialBalance run = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId && !r.Deleted, ct);
            if (run == null) return ResponseDtoHelper.NotFound<BalanceDraftsDto>(BalanceMessages.NotFound);
            // a run that is empty, or that was only half saved, is not a basis for purchase requests
            int storedLines = await Uow.Set<ProjectMaterialBalanceLine>().AsNoTracking().CountAsync(l => l.BalanceId == runId && !l.Deleted, ct);
            if (storedLines == 0 || storedLines != run.LineCount) return ResponseDtoHelper.BadRequest<BalanceDraftsDto>(BalanceMessages.RunEmpty);
            int latest = await Uow.Set<ProjectMaterialBalance>().AsNoTracking().Where(x => x.BaselineId == run.BaselineId && !x.Deleted).MaxAsync(x => x.RunNumber, ct);
            if (run.RunNumber != latest) return ResponseDtoHelper.BadRequest<BalanceDraftsDto>("پیش‌نویس خرید فقط از آخرین اجرای بالانس ساخته می‌شود؛ بالانس را دوباره اجرا کنید.");
            PbProjectBaseline b = await Uow.Set<PbProjectBaseline>().AsNoTracking().FirstAsync(x => x.Id == run.BaselineId, ct);
            string reviewError = await PcGates.RequireReviewAsync(Uow, run.ContractId, ct);
            if (reviewError != null) return ResponseDtoHelper.BadRequest<BalanceDraftsDto>(reviewError);

            List<ProjectMaterialBalanceLine> lines = await Uow.Set<ProjectMaterialBalanceLine>().Where(l => l.BalanceId == runId && !l.Deleted).OrderBy(l => l.SortOrder).ToListAsync(ct);
            foreach (ProjectMaterialBalanceLine l in lines) { l.PurchaseRequestId = null; l.PurchaseRequestLineId = null; }
            List<ProjectMaterialBalanceLine> shortLines = lines.Where(l => l.ItemId.HasValue && l.ShortQty > 0 && l.Kind != BalanceRequirementKind.NotCoded).ToList();
            Dictionary<int, ItemFacts> items = await LoadItemsAsync(shortLines.Select(l => l.ItemId.Value), ct);
            var dto = new BalanceDraftsDto { RunId = runId, WorkOrderId = run.WorkOrderId };
            DateTimeOffset now = DateTimeOffset.Now;
            DateTime today = DateTime.Today;

            // inactive items are not ordered
            foreach (var skip in shortLines.Where(l => !items.TryGetValue(l.ItemId.Value, out ItemFacts f) || !f.IsActive).Select(l => l.ItemId.Value).Distinct().ToList())
                dto.Notes.Add("کالای " + (items.TryGetValue(skip, out ItemFacts sf) ? sf.Code : "#" + skip) + " غیرفعال است و درخواست نمی‌شود.");
            shortLines = shortLines.Where(l => items.TryGetValue(l.ItemId.Value, out ItemFacts f) && f.IsActive).ToList();

            List<int> itemIds = shortLines.Select(l => l.ItemId.Value).Distinct().ToList();
            Dictionary<int, decimal> costs = itemIds.Count == 0 ? new Dictionary<int, decimal>() : await Uow.Set<ItemCost>().Where(c => itemIds.Contains(c.ItemId)).ToDictionaryAsync(c => c.ItemId, c => c.LastPurchasePrice ?? c.AverageCost, ct);
            List<PurchaseRequest> drafts = await Uow.Set<PurchaseRequest>().Include(r => r.Lines)
                .Where(r => r.ProjectBaselineId == b.Id && r.Status == PurchaseRequestStatus.Draft && !r.Deleted).OrderBy(r => r.Id).ToListAsync(ct);

            // which draft requests and lines carry a file somebody attached by hand (one query for all of them)
            var humanOwners = new HashSet<(AttachmentOwnerType, int)>();
            {
                List<int> lineIds = drafts.SelectMany(d => d.Lines).Select(l => l.Id).ToList();
                List<int> requestIds = drafts.Select(d => d.Id).ToList();
                var files = await Uow.Set<RecordAttachment>().AsNoTracking().Where(a => !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null && a.LinkedFromAttachmentId == null
                    && ((a.OwnerType == AttachmentOwnerType.PurchaseRequestLine && lineIds.Contains(a.OwnerId)) || (a.OwnerType == AttachmentOwnerType.PurchaseRequest && requestIds.Contains(a.OwnerId))))
                    .Select(a => new { a.OwnerType, a.OwnerId }).ToListAsync(ct);
                foreach (var f in files) humanOwners.Add((f.OwnerType, f.OwnerId));
            }

            string woCode = await Uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => w.Id == run.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            var handled = new HashSet<PurchaseRequest>();
            var createdSet = new HashSet<PurchaseRequest>();
            var plan = new List<(PurchaseRequest Request, Dictionary<int, List<ProjectMaterialBalanceLine>> Rows)>();
            foreach (var group in shortLines.GroupBy(l => s.GroupKey(items[l.ItemId.Value].GroupCode)).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                PurchaseRequest pr = drafts.FirstOrDefault(d => d.BalanceGroupKey == group.Key && !handled.Contains(d));
                bool created = pr == null;
                if (created)
                {
                    pr = new PurchaseRequest
                    {
                        Number = await InventoryNumbers.NextNumberAsync(Uow, ProcurementNumbers.PurchaseRequest, "PRQ", today, ct), Source = PurchaseRequestSource.Bom, Kind = PurchaseKind.Goods, Status = PurchaseRequestStatus.Draft,
                        RequestDate = today, WorkOrderId = run.WorkOrderId, ContractId = run.ContractId, ProjectBaselineId = b.Id, BalanceGroupKey = group.Key, Lines = new List<PurchaseRequestLine>(),
                        CreateDate = now, CreatorUserId = Me,
                    };
                    Uow.Set<PurchaseRequest>().Add(pr);
                    createdSet.Add(pr);
                }
                // the maker of a draft stays the maker: a later automatic update does not change who created it
                handled.Add(pr);

                var byItem = group.GroupBy(l => l.ItemId.Value).ToDictionary(g => g.Key, g => g.OrderBy(l => l.UseByDate ?? DateTime.MaxValue).ThenBy(l => l.SortOrder).ToList());
                // lines of items that are no longer short go (a line that carries a file somebody attached stays and is reported)
                // only the lines this layer made (they point at a balance line) and nobody edited are removed; a line added or edited by a person stays
                foreach (PurchaseRequestLine old in pr.Lines.Where(l => (!l.ItemId.HasValue || !byItem.ContainsKey(l.ItemId.Value))).ToList())
                {
                    if (old.BalanceLineId == null) continue;
                    if (old.ManuallyEdited) { dto.Notes.Add("ردیف " + old.RowNumber + " درخواست " + pr.Number + " دیگر کمبود نیست ولی دستی ویرایش شده و نگه داشته شد."); continue; }
                    if (humanOwners.Contains((AttachmentOwnerType.PurchaseRequestLine, old.Id))) { dto.Notes.Add("ردیف " + old.RowNumber + " درخواست " + pr.Number + " دیگر کمبود نیست ولی پیوند دستی دارد و نگه داشته شد."); continue; }
                    await RemoveLinksAsync(AttachmentOwnerType.PurchaseRequestLine, old.Id, now, ct);
                    Uow.Set<PurchaseRequestLine>().Remove(old);
                    pr.Lines.Remove(old);
                }
                foreach (var kv in byItem.OrderBy(k => items[k.Key].Code, StringComparer.Ordinal))
                {
                    ItemFacts item = items[kv.Key];
                    List<ProjectMaterialBalanceLine> rows = kv.Value;
                    PurchaseRequestLine line = pr.Lines.FirstOrDefault(l => l.ItemId == kv.Key);
                    if (line != null && (line.ManuallyEdited || line.BalanceLineId == null))
                    {
                        // a person added or changed this line: the quantity and the dates are theirs; the run only points at it
                        line.BalanceLineId = line.BalanceLineId ?? rows[0].Id;
                        if (line.Quantity < BalanceRules.Round(rows.Sum(r => r.ShortQty))) dto.Notes.Add("ردیف کالای " + item.Code + " درخواست " + pr.Number + " دستی ویرایش شده و کمتر از کمبود امروز است (" + BalanceRules.Num(rows.Sum(r => r.ShortQty)) + ").");
                        continue;
                    }
                    if (line == null) { line = new PurchaseRequestLine(); pr.Lines.Add(line); }
                    DateTime? needed = rows.Where(r => r.NeedDate.HasValue).Select(r => r.NeedDate).Min();
                    string parts = string.Join("، ", rows.Where(r => r.PartNumber != null).Select(r => r.PartNumber).Distinct().Take(8));
                    line.ItemId = kv.Key;
                    line.Description = item.Title;
                    line.Unit = item.Unit ?? "عدد";
                    line.Quantity = BalanceRules.RoundUnit(rows.Sum(r => r.ShortQty), line.Unit);
                    line.NeededBy = needed; // the day the part is used minus the lead time of the item (each balance line carries it as its need date)
                    line.WbsNodeId = rows.Select(r => r.WbsNodeId).FirstOrDefault(n => n.HasValue);
                    line.BalanceLineId = rows[0].Id;
                    if (!line.EstimatedUnitPrice.HasValue && costs.TryGetValue(kv.Key, out decimal c) && c > 0) line.EstimatedUnitPrice = c;
                    line.TechnicalRequirements = Cut("BOM " + run.BomCode + " نسخه " + run.BomRevision.ToString("00", CultureInfo.InvariantCulture) + (parts.Length > 0 ? "؛ قطعه: " + parts : "")
                        + (string.IsNullOrWhiteSpace(item.Material) ? "" : "؛ ماده: " + item.Material) + (string.IsNullOrWhiteSpace(item.Standard) ? "" : "؛ استاندارد: " + item.Standard), 4000);
                }
                int row = 1;
                foreach (PurchaseRequestLine l in pr.Lines.OrderBy(l => items.TryGetValue(l.ItemId ?? 0, out ItemFacts f) ? f.Code : "~", StringComparer.Ordinal)) l.RowNumber = row++;
                pr.Priority = group.Max(l => l.Priority);
                pr.EstimatedAmount = Math.Round(pr.Lines.Sum(l => l.Quantity * (l.EstimatedUnitPrice ?? 0)), 0);
                pr.Description = Cut("پیش‌نویس خودکار بالانس مواد پروژه (دستور کار " + woCode + "، BOM " + run.BomCode + " نسخه " + run.BomRevision.ToString("00", CultureInfo.InvariantCulture) + "، اجرای " + run.RunNumber
                    + "، گروه " + group.Key + "). مقدار هر ردیف کمبود پس از کسر موجودی، رزرو، درخواست‌ها و سفارش‌های در راه است. بررسی، ویرایش و ارسال با واحد برنامه‌ریزی است؛ سامانه خودش ارسال نمی‌کند.", 1000);
                pr.ModifyDate = now; pr.ModifierUserId = Me;
                plan.Add((pr, byItem));
                if (created) dto.Created++; else dto.Updated++;
            }

            // a draft of a group that has no shortage any more is withdrawn (its own lines only; a draft with a hand-attached file stays)
            foreach (PurchaseRequest old in drafts.Where(d => !handled.Contains(d)).ToList())
            {
                bool humanFile = humanOwners.Contains((AttachmentOwnerType.PurchaseRequest, old.Id)) || old.Lines.Any(l => humanOwners.Contains((AttachmentOwnerType.PurchaseRequestLine, l.Id)));
                // a request with a line that a person added or edited is theirs now
                bool humanLine = old.Lines.Any(l => l.ManuallyEdited || l.BalanceLineId == null);
                if (humanLine) { dto.Notes.Add("درخواست " + old.Number + " دیگر کمبودی ندارد ولی ردیف دستی دارد و نگه داشته شد."); continue; }
                if (humanFile) { dto.Notes.Add("درخواست " + old.Number + " دیگر کمبودی ندارد ولی پیوند دستی دارد و نگه داشته شد."); continue; }
                foreach (PurchaseRequestLine l in old.Lines.ToList()) await RemoveLinksAsync(AttachmentOwnerType.PurchaseRequestLine, l.Id, now, ct);
                await RemoveLinksAsync(AttachmentOwnerType.PurchaseRequest, old.Id, now, ct);
                old.Delete(); old.ModifyDate = now; old.ModifierUserId = Me;
                dto.RemovedEmpty++;
            }

            await Uow.SaveChangesAsync(ct);

            // the balance lines point at the draft lines; then the documents are linked
            foreach (var (pr, byItem) in plan)
            {
                int linked = 0;
                foreach (var kv in byItem)
                {
                    PurchaseRequestLine line = pr.Lines.FirstOrDefault(l => l.ItemId == kv.Key);
                    foreach (ProjectMaterialBalanceLine bl in kv.Value) { bl.PurchaseRequestId = pr.Id; bl.PurchaseRequestLineId = line?.Id; }
                }
                if (s.LinkDocuments) linked = await LinkDocumentsAsync(b, run, pr, byItem, items, now, ct);
                dto.Requests.Add(new BalanceDraftRequestDto { Id = pr.Id, Number = pr.Number, GroupKey = pr.BalanceGroupKey, Created = createdSet.Contains(pr), LineCount = pr.Lines.Count, LinkedDocuments = linked, Priority = pr.Priority });
            }
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(dto);
        }

        private async Task RemoveLinksAsync(AttachmentOwnerType type, int ownerId, DateTimeOffset now, CancellationToken ct)
        {
            List<RecordAttachment> rows = await Uow.Set<RecordAttachment>().Where(a => a.OwnerType == type && a.OwnerId == ownerId && !a.Deleted && a.RemovedAt == null).ToListAsync(ct);
            foreach (RecordAttachment a in rows.Where(IsLink)) { a.RemovedAt = now; a.RemovedByUserId = Me; }
        }

        /// <summary>
        /// Links the technical documents to the request: the BOM revision documents to the request itself and, to each line, the files of its item (the drawing, the specification), of the part item and of the OPC charts of the part.
        /// A link is a second attachment row on the same stored file (nothing is copied); a document that is voided or replaced at its source leaves the draft. Prices never travel.
        /// </summary>
        private async Task<int> LinkDocumentsAsync(PbProjectBaseline b, ProjectMaterialBalance run, PurchaseRequest pr, Dictionary<int, List<ProjectMaterialBalanceLine>> byItem, Dictionary<int, ItemFacts> items, DateTimeOffset now, CancellationToken ct)
        {
            int total = 0;
            List<RecordAttachment> bomDocs = (await Uow.Set<RecordAttachment>().AsNoTracking().Where(a => a.OwnerType == AttachmentOwnerType.PbBomHeader && a.OwnerId == b.BomHeaderId).ToListAsync(ct))
                .Where(a => InForce(a) && !AttachmentRules.CarriesPrices(a.Kind)).ToList();
            total += await SyncLinksAsync(AttachmentOwnerType.PurchaseRequest, pr.Id, bomDocs, "BOM " + run.BomCode, now, ct);

            // the OPC charts of the project, by the part numbers they carry steps for
            List<int> routingIds = await Uow.Set<PbProjectRouting>().AsNoTracking().Where(r => r.BaselineId == b.Id && !r.Deleted).Select(r => r.OpcRoutingId).ToListAsync(ct);
            var routingParts = routingIds.Count == 0 ? new List<(int RoutingId, string PartNumber)>() : (await Uow.Set<PbOpcStep>().AsNoTracking().Where(st => routingIds.Contains(st.RoutingId) && !st.Deleted && st.PartNumber != null)
                .Select(st => new { st.RoutingId, st.PartNumber }).Distinct().ToListAsync(ct)).Select(x => (x.RoutingId, x.PartNumber)).ToList();

            foreach (var kv in byItem)
            {
                PurchaseRequestLine line = pr.Lines.FirstOrDefault(l => l.ItemId == kv.Key);
                if (line == null || line.Id == 0) continue;
                var ownerItems = new HashSet<int> { kv.Key };
                if (items.TryGetValue(kv.Key, out ItemFacts f) && f.PartItemId.HasValue) ownerItems.Add(f.PartItemId.Value);
                List<string> parts = kv.Value.Where(r => r.PartNumber != null).Select(r => r.PartNumber).Distinct().ToList();
                if (parts.Count > 0)
                    foreach (int id in await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.StageCode2 == null && i.PartNumber != null && parts.Contains(i.PartNumber)).Select(i => i.Id).ToListAsync(ct)) ownerItems.Add(id);
                List<int> routings = routingParts.Where(x => parts.Contains(x.PartNumber)).Select(x => x.RoutingId).Distinct().ToList();
                List<RecordAttachment> docs = (await Uow.Set<RecordAttachment>().AsNoTracking().Where(a => (a.OwnerType == AttachmentOwnerType.InventoryItem && ownerItems.Contains(a.OwnerId))
                        || (a.OwnerType == AttachmentOwnerType.PbOpcRouting && routings.Contains(a.OwnerId))).ToListAsync(ct))
                    .Where(a => InForce(a) && !AttachmentRules.CarriesPrices(a.Kind)).OrderBy(a => a.Id).ToList();
                total += await SyncLinksAsync(AttachmentOwnerType.PurchaseRequestLine, line.Id, docs, parts.Count > 0 ? parts[0] : "کالا", now, ct);
            }
            return total;
        }

        private async Task<int> SyncLinksAsync(AttachmentOwnerType type, int ownerId, List<RecordAttachment> sources, string label, DateTimeOffset now, CancellationToken ct)
        {
            List<RecordAttachment> existing = await Uow.Set<RecordAttachment>().Where(a => a.OwnerType == type && a.OwnerId == ownerId && !a.Deleted).ToListAsync(ct);
            var wanted = new HashSet<Guid>(sources.Select(a => a.DocumentId));
            foreach (RecordAttachment link in existing.Where(a => InForce(a) && IsLink(a) && !wanted.Contains(a.DocumentId)))
            {
                link.RemovedAt = now; link.RemovedByUserId = Me;
            }
            int count = existing.Count(a => InForce(a) && a.RemovedAt == null && wanted.Contains(a.DocumentId));
            var added = new HashSet<Guid>();
            foreach (RecordAttachment src in sources)
            {
                if (existing.Any(a => a.DocumentId == src.DocumentId && InForce(a) && a.RemovedAt == null) || !added.Add(src.DocumentId)) continue;
                Uow.Set<RecordAttachment>().Add(new RecordAttachment
                {
                    OwnerType = type, OwnerId = ownerId, DocumentId = src.DocumentId, LinkedFromAttachmentId = src.Id, LinkObsolete = false, Kind = src.Kind, Title = Cut(LinkPrefix + label + ": " + (src.Title ?? src.FileName), 300), Revision = src.Revision,
                    FileName = src.FileName, SizeBytes = src.SizeBytes, UploadedByUserId = Me, UploadedAt = now, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                });
                count++;
            }
            return count;
        }
    }
}
