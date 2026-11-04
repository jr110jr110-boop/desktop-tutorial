using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    // Links to parts, items, WBS, activities and tags (E12, P3); the tag register; distribution list, approver matrix and notices (E6, E14).
    public partial class EdmsService
    {
        // ============================================================================ tags

        public async Task<ResponseDto<List<PcTagDto>>> GetTagsAsync(int contractId, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.Readers)) return Bad<List<PcTagDto>>(PcText.NotAllowed);
            List<PcEquipmentTag> tags = await _uow.Set<PcEquipmentTag>().AsNoTracking().Where(t => !t.Deleted && t.ContractId == contractId).OrderBy(t => t.Code).ToListAsync(ct);
            List<int> ids = tags.Select(t => t.Id).ToList();
            Dictionary<int, int> counts = ids.Count == 0 ? new Dictionary<int, int>()
                : (await _uow.Set<PcDocumentLink>().AsNoTracking().Where(l => !l.Deleted && l.TagId != null && ids.Contains(l.TagId.Value)).Select(l => l.TagId.Value).ToListAsync(ct)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            List<int> itemIds = tags.Where(t => t.ItemId.HasValue).Select(t => t.ItemId.Value).Distinct().ToList();
            Dictionary<int, string> items = itemIds.Count == 0 ? new Dictionary<int, string>() : (await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code }).ToListAsync(ct)).ToDictionary(i => i.Id, i => i.Code);
            List<int> woIds = tags.Where(t => t.WorkOrderId.HasValue).Select(t => t.WorkOrderId.Value).Distinct().ToList();
            Dictionary<int, string> wos = woIds.Count == 0 ? new Dictionary<int, string>() : (await _uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).Select(w => new { w.Id, w.Code }).ToListAsync(ct)).ToDictionary(w => w.Id, w => w.Code);
            return ResponseDtoHelper.Ok(tags.Select(t => new PcTagDto
            {
                Id = t.Id, ContractId = t.ContractId, Code = t.Code, Title = t.Title, ItemId = t.ItemId, WorkOrderId = t.WorkOrderId, Note = t.Note, IsActive = t.IsActive,
                ItemCode = t.ItemId.HasValue && items.TryGetValue(t.ItemId.Value, out string ic) ? ic : null, WorkOrderCode = t.WorkOrderId.HasValue && wos.TryGetValue(t.WorkOrderId.Value, out string wc) ? wc : null,
                DocumentCount = counts.TryGetValue(t.Id, out int n) ? n : 0,
            }).ToList());
        }

        public async Task<ResponseDto<PcTagDto>> SaveTagAsync(PcTagSaveDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Code) == null) return Bad<PcTagDto>(PcText.InvalidData);
            if (!_user.IsInRole(EdmsRoles.Preparers)) return Bad<PcTagDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId && !c.Deleted, ct)) return PcText.NotFound<PcTagDto>(PcText.ContractNotFound);
            string code = PcText.Upper(dto.Code);
            if (code.Length > 60) return Bad<PcTagDto>("کد تگ حداکثر ۶۰ نویسه است.");
            if (await _uow.Set<PcEquipmentTag>().AnyAsync(t => !t.Deleted && t.ContractId == dto.ContractId && t.Code == code && t.Id != (dto.Id ?? 0), ct)) return Bad<PcTagDto>("این تگ در پروژه قبلاً ثبت شده است.");
            if (dto.ItemId.HasValue && !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, ct)) return PcText.NotFound<PcTagDto>("کالا پیدا نشد.");
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && w.ContractId == dto.ContractId && !w.Deleted, ct)) return Bad<PcTagDto>("دستور کار متعلق به این پروژه نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcEquipmentTag t;
            if (dto.Id.HasValue)
            {
                t = await _uow.Set<PcEquipmentTag>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, ct);
                if (t == null) return PcText.NotFound<PcTagDto>("تگ پیدا نشد.");
                if (t.ContractId != dto.ContractId) return Bad<PcTagDto>("تگ به پروژه دیگری منتقل نمی‌شود.");
            }
            else { t = new PcEquipmentTag { ContractId = dto.ContractId, CreateDate = now, CreatorUserId = Me }; _uow.Set<PcEquipmentTag>().Add(t); }
            t.Code = code; t.Title = Clean(dto.Title); t.ItemId = dto.ItemId; t.WorkOrderId = dto.WorkOrderId; t.Note = Clean(dto.Note); t.IsActive = dto.IsActive; t.ModifyDate = now; t.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await GetTagsAsync(t.ContractId, ct)).Data.First(x => x.Id == t.Id));
        }

        // ============================================================================ links

        private async Task<List<PcDocLinkDto>> LinkDtosAsync(List<PcDocumentLink> links, CancellationToken ct)
        {
            if (links.Count == 0) return new List<PcDocLinkDto>();
            List<int> dIds = links.Where(l => l.DeliverableId.HasValue).Select(l => l.DeliverableId.Value).Distinct().ToList();
            List<int> cIds = links.Where(l => l.ControlledDocumentId.HasValue).Select(l => l.ControlledDocumentId.Value).Distinct().ToList();
            List<int> itemIds = links.Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value).Distinct().ToList();
            List<int> tagIds = links.Where(l => l.TagId.HasValue).Select(l => l.TagId.Value).Distinct().ToList();
            var docs = dIds.Count == 0 ? new Dictionary<int, (string, string)>() : (await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => dIds.Contains(d.Id)).Select(d => new { d.Id, d.DocumentNumber, d.Title }).ToListAsync(ct)).ToDictionary(d => d.Id, d => (d.DocumentNumber, d.Title));
            var cdocs = cIds.Count == 0 ? new Dictionary<int, (string, string)>() : (await _uow.Set<ControlledDocument>().AsNoTracking().Where(d => cIds.Contains(d.Id)).Select(d => new { d.Id, d.Code, d.Title }).ToListAsync(ct)).ToDictionary(d => d.Id, d => (d.Code, d.Title));
            Dictionary<int, string> items = itemIds.Count == 0 ? new Dictionary<int, string>() : (await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code }).ToListAsync(ct)).ToDictionary(i => i.Id, i => i.Code);
            Dictionary<int, string> tags = tagIds.Count == 0 ? new Dictionary<int, string>() : (await _uow.Set<PcEquipmentTag>().AsNoTracking().Where(t => tagIds.Contains(t.Id)).Select(t => new { t.Id, t.Code }).ToListAsync(ct)).ToDictionary(t => t.Id, t => t.Code);
            return links.Select(l =>
            {
                (string Number, string Title) doc = l.DeliverableId.HasValue && docs.TryGetValue(l.DeliverableId.Value, out var a) ? a : l.ControlledDocumentId.HasValue && cdocs.TryGetValue(l.ControlledDocumentId.Value, out var b) ? b : (null, null);
                return new PcDocLinkDto
                {
                    Id = l.Id, DeliverableId = l.DeliverableId, ControlledDocumentId = l.ControlledDocumentId, DocumentNumber = doc.Number, Title = doc.Title, Kind = l.Kind, ItemId = l.ItemId,
                    ItemCode = l.ItemId.HasValue && items.TryGetValue(l.ItemId.Value, out string ic) ? ic : null, BomLineId = l.BomLineId, PartNumber = l.PartNumber, StageCode2 = l.StageCode2, WbsNodeId = l.WbsNodeId,
                    ActivityId = l.ActivityId, TagId = l.TagId, TagCode = l.TagId.HasValue && tags.TryGetValue(l.TagId.Value, out string tc) ? tc : null, OperationTemplateId = l.OperationTemplateId, Note = l.Note,
                };
            }).ToList();
        }

        private async Task<List<PcDocLinkDto>> LinksOfDeliverableAsync(int deliverableId, CancellationToken ct) =>
            await LinkDtosAsync(await _uow.Set<PcDocumentLink>().AsNoTracking().Where(l => !l.Deleted && l.DeliverableId == deliverableId).OrderBy(l => l.Id).ToListAsync(ct), ct);

        public async Task<ResponseDto<PcDocLinkDto>> SaveLinkAsync(PcDocLinkSaveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcDocLinkDto>(PcText.InvalidData);
            if (!_user.IsInRole(EdmsRoles.Preparers)) return Bad<PcDocLinkDto>(PcText.NotAllowed);
            if (dto.DeliverableId.HasValue == dto.ControlledDocumentId.HasValue) return Bad<PcDocLinkDto>("یک سند MDR یا یک سند کنترل‌شده را مشخص کنید (فقط یکی).");
            if (!Enum.IsDefined(typeof(PcDocLinkKind), dto.Kind)) return Bad<PcDocLinkDto>("نوع پیوند نامعتبر است.");
            int? contractId = null;
            if (dto.DeliverableId.HasValue)
            {
                var d = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(x => x.Id == dto.DeliverableId.Value && !x.Deleted).Select(x => new { x.ContractId, x.CheckedOutByUserId }).FirstOrDefaultAsync(ct);
                if (d == null) return PcText.NotFound<PcDocLinkDto>("سند پیدا نشد.");
                if (d.CheckedOutByUserId.HasValue && d.CheckedOutByUserId != Me) return Bad<PcDocLinkDto>(LockedBy);
                contractId = d.ContractId;
            }
            else if (!await _uow.Set<ControlledDocument>().AnyAsync(c => c.Id == dto.ControlledDocumentId.Value && !c.Deleted, ct)) return PcText.NotFound<PcDocLinkDto>("سند کنترل‌شده پیدا نشد.");

            var link = new PcDocumentLink { DeliverableId = dto.DeliverableId, ControlledDocumentId = dto.ControlledDocumentId, Kind = dto.Kind, OperationTemplateId = dto.OperationTemplateId, Note = Clean(dto.Note) };
            string part = Clean(dto.PartNumber);
            string stage = PcText.Upper(dto.StageCode2);
            switch (dto.Kind)
            {
                case PcDocLinkKind.Item:
                    if (!dto.ItemId.HasValue || !await _uow.Set<InventoryItem>().AnyAsync(i => i.Id == dto.ItemId.Value && !i.Deleted, ct)) return PcText.NotFound<PcDocLinkDto>("کالا پیدا نشد.");
                    link.ItemId = dto.ItemId;
                    break;
                case PcDocLinkKind.BomLine:
                {
                    var line = dto.BomLineId.HasValue ? await _uow.Set<PbBomLine>().AsNoTracking().Where(l => l.Id == dto.BomLineId.Value && !l.Deleted).Select(l => new { l.PartNumber, l.IsHeading }).FirstOrDefaultAsync(ct) : null;
                    if (line == null) return PcText.NotFound<PcDocLinkDto>("ردیف BOM پیدا نشد.");
                    if (line.IsHeading || Clean(line.PartNumber) == null) return Bad<PcDocLinkDto>("ردیف سرفصل یا بدون شماره قطعه به سند وصل نمی‌شود.");
                    link.BomLineId = dto.BomLineId; link.PartNumber = Clean(line.PartNumber);
                    break;
                }
                case PcDocLinkKind.PartNumber:
                    if (part == null) return Bad<PcDocLinkDto>("شماره قطعه (BOM) را بنویسید.");
                    if (part.Length > 60) return Bad<PcDocLinkDto>("شماره قطعه حداکثر ۶۰ نویسه است.");
                    link.PartNumber = part;
                    break;
                case PcDocLinkKind.WbsNode:
                {
                    var n = dto.WbsNodeId.HasValue ? await _uow.Set<PcWbsNode>().AsNoTracking().Where(x => x.Id == dto.WbsNodeId.Value && !x.Deleted).Select(x => new { x.ContractId }).FirstOrDefaultAsync(ct) : null;
                    if (n == null) return PcText.NotFound<PcDocLinkDto>("گره WBS پیدا نشد.");
                    if (contractId.HasValue && n.ContractId != contractId.Value) return Bad<PcDocLinkDto>("گره WBS متعلق به پروژه سند نیست.");
                    link.WbsNodeId = dto.WbsNodeId;
                    break;
                }
                case PcDocLinkKind.Activity:
                {
                    var a = dto.ActivityId.HasValue ? await _uow.Set<PcActivity>().AsNoTracking().Where(x => x.Id == dto.ActivityId.Value && !x.Deleted).Select(x => new { x.ContractId }).FirstOrDefaultAsync(ct) : null;
                    if (a == null) return PcText.NotFound<PcDocLinkDto>("فعالیت پیدا نشد.");
                    if (contractId.HasValue && a.ContractId != contractId.Value) return Bad<PcDocLinkDto>("فعالیت متعلق به پروژه سند نیست.");
                    link.ActivityId = dto.ActivityId;
                    break;
                }
                case PcDocLinkKind.EquipmentTag:
                {
                    var t = dto.TagId.HasValue ? await _uow.Set<PcEquipmentTag>().AsNoTracking().Where(x => x.Id == dto.TagId.Value && !x.Deleted).Select(x => new { x.ContractId }).FirstOrDefaultAsync(ct) : null;
                    if (t == null) return PcText.NotFound<PcDocLinkDto>("تگ پیدا نشد.");
                    if (contractId.HasValue && t.ContractId != contractId.Value) return Bad<PcDocLinkDto>("تگ متعلق به پروژه سند نیست.");
                    link.TagId = dto.TagId;
                    break;
                }
            }
            if (stage != null)
            {
                if (dto.Kind != PcDocLinkKind.PartNumber && dto.Kind != PcDocLinkKind.BomLine) return Bad<PcDocLinkDto>("کد مرحله فقط برای پیوند به شماره قطعه است.");
                if (!await _uow.Set<PbStage>().AnyAsync(s => s.Code == stage && !s.Deleted, ct)) return Bad<PcDocLinkDto>("کد مرحله OPC تعریف نشده است.");
                link.StageCode2 = stage;
            }
            bool duplicate = await _uow.Set<PcDocumentLink>().AnyAsync(l => !l.Deleted && l.DeliverableId == link.DeliverableId && l.ControlledDocumentId == link.ControlledDocumentId && l.Kind == link.Kind
                && l.ItemId == link.ItemId && l.PartNumber == link.PartNumber && l.StageCode2 == link.StageCode2 && l.WbsNodeId == link.WbsNodeId && l.ActivityId == link.ActivityId && l.TagId == link.TagId
                && l.OperationTemplateId == link.OperationTemplateId, ct);
            if (duplicate) return Bad<PcDocLinkDto>("این پیوند قبلاً ثبت شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            link.CreateDate = now; link.CreatorUserId = Me; link.ModifyDate = now; link.ModifierUserId = Me;
            _uow.Set<PcDocumentLink>().Add(link);
            if (link.DeliverableId.HasValue) Log(link.DeliverableId.Value, null, PcDocEventKind.Linked, link.Kind + " " + (link.PartNumber ?? link.ItemId?.ToString() ?? link.TagId?.ToString() ?? link.ActivityId?.ToString() ?? link.WbsNodeId?.ToString()), now);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await LinkDtosAsync(new List<PcDocumentLink> { link }, ct)).First());
        }

        public async Task<ResponseDto<bool>> VoidLinkAsync(PcDocLinkVoidDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<bool>(PcText.InvalidData);
            if (!_user.IsInRole(EdmsRoles.Preparers)) return Bad<bool>(PcText.NotAllowed);
            PcDocumentLink l = await _uow.Set<PcDocumentLink>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (l == null) return PcText.NotFound<bool>("پیوند پیدا نشد.");
            string reason = Clean(dto.Reason);
            if (reason == null || reason.Length < 3) return Bad<bool>("علت حذف پیوند الزامی است (حداقل ۳ حرف).");
            if (l.DeliverableId.HasValue)
            {
                var holder = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(x => x.Id == l.DeliverableId.Value).Select(x => x.CheckedOutByUserId).FirstOrDefaultAsync(ct);
                if (holder.HasValue && holder != Me) return Bad<bool>(LockedBy);
            }
            DateTimeOffset now = DateTimeOffset.Now;
            l.Delete(); l.UnlinkReason = reason; l.UnlinkedByUserId = Me; l.ModifyDate = now; l.ModifierUserId = Me;
            if (l.DeliverableId.HasValue) Log(l.DeliverableId.Value, null, PcDocEventKind.Unlinked, reason, now);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }

        public async Task<ResponseDto<List<PcDocLinkDto>>> GetLinksAsync(PcDocLinkQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.DocReaders)) return Bad<List<PcDocLinkDto>>(PcText.NotAllowed);
            query ??= new PcDocLinkQuery();
            IQueryable<PcDocumentLink> q = _uow.Set<PcDocumentLink>().AsNoTracking().Where(l => !l.Deleted);
            if (query.DeliverableId.HasValue) q = q.Where(l => l.DeliverableId == query.DeliverableId.Value);
            if (query.ItemId.HasValue) q = q.Where(l => l.ItemId == query.ItemId.Value);
            string part = Clean(query.PartNumber);
            if (part != null) q = q.Where(l => l.PartNumber == part);
            if (query.TagId.HasValue) q = q.Where(l => l.TagId == query.TagId.Value);
            if (query.ActivityId.HasValue) q = q.Where(l => l.ActivityId == query.ActivityId.Value);
            if (query.WbsNodeId.HasValue) q = q.Where(l => l.WbsNodeId == query.WbsNodeId.Value);
            return ResponseDtoHelper.Ok(await LinkDtosAsync(await q.OrderBy(l => l.Id).Take(500).ToListAsync(ct), ct));
        }

        /// <summary>E12: all documents of a part, a valve (tag), an activity or a WBS node, each with the revision in force.</summary>
        public async Task<ResponseDto<List<PcPartDocumentDto>>> GetPartDocumentsAsync(PcDocLinkQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.Readers)) return Bad<List<PcPartDocumentDto>>(PcText.NotAllowed);
            query ??= new PcDocLinkQuery();
            var keys = new EdmsKeys { ItemId = query.ItemId, PartNumber = Clean(query.PartNumber), StageCode2 = PcText.Upper(query.StageCode2), ActivityId = query.ActivityId, WbsNodeId = query.WbsNodeId };
            if (query.ItemId.HasValue && keys.PartNumber == null)
            {
                var item = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == query.ItemId.Value).Select(i => new { i.PartNumber, i.StageCode2 }).FirstOrDefaultAsync(ct);
                if (item == null) return PcText.NotFound<List<PcPartDocumentDto>>("کالا پیدا نشد.");
                keys.PartNumber = Clean(item.PartNumber); keys.StageCode2 = item.StageCode2;
            }
            if (query.TagId.HasValue)
            {
                var tag = await _uow.Set<PcEquipmentTag>().AsNoTracking().Where(t => t.Id == query.TagId.Value && !t.Deleted).Select(t => new { t.ItemId }).FirstOrDefaultAsync(ct);
                if (tag == null) return PcText.NotFound<List<PcPartDocumentDto>>("تگ پیدا نشد.");
                keys.TagIds.Add(query.TagId.Value);
                if (tag.ItemId.HasValue && !keys.ItemId.HasValue) keys.ItemId = tag.ItemId;
            }
            if (!keys.ItemId.HasValue && keys.PartNumber == null && keys.TagIds.Count == 0 && !keys.ActivityId.HasValue && !keys.WbsNodeId.HasValue) return Bad<List<PcPartDocumentDto>>("کالا، شماره قطعه، تگ، فعالیت یا گره WBS را مشخص کنید.");
            List<PcDocumentLink> links = await EdmsResolver.MatchingLinksAsync(_uow, keys, null, false, ct);
            var found = new Dictionary<string, EdmsFoundDocument>();
            foreach (PcDocumentLink l in links)
            {
                string key = l.DeliverableId.HasValue ? "d" + l.DeliverableId.Value : "c" + l.ControlledDocumentId.Value;
                if (!found.ContainsKey(key)) found[key] = new EdmsFoundDocument { Via = l.Kind, ViaText = l.Kind.ToString(), Deliverable = l.DeliverableId.HasValue ? new PcEngineeringDeliverable { Id = l.DeliverableId.Value } : null, ControlledDocumentId = l.ControlledDocumentId };
            }
            List<EdmsFoundDocument> docs = found.Values.ToList();
            await EdmsResolver.FillAsync(_uow, docs, ct);
            return ResponseDtoHelper.Ok(docs.Where(f => f.Number != null).OrderBy(f => f.Number, StringComparer.Ordinal).Select(f => new PcPartDocumentDto
            {
                DeliverableId = f.Deliverable?.Id, ControlledDocumentId = f.ControlledDocumentId, DocumentNumber = f.Number, Title = f.Title, Discipline = f.Discipline, DocumentType = f.DocumentType, Via = f.Via, ViaText = f.ViaText,
                ReleasedRevisionId = f.Released?.Id, ReleasedCode = f.Released?.Code ?? f.ControlledRevision?.Revision, ReleasedAt = f.Released?.ReleasedAt, LatestStatus = f.LatestStatus,
                Files = f.Files.Select(x => new PcRevisionFileDto { Id = x.Id, DocumentId = x.DocumentId, Role = x.Role, FileName = x.FileName, Title = x.Title }).ToList(),
            }).ToList());
        }

        // ============================================================================ distribution list and approver matrix

        public async Task<ResponseDto<List<PcDistributionDto>>> GetDistributionAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.DocReaders)) return Bad<List<PcDistributionDto>>(PcText.NotAllowed);
            List<PcDocumentDistribution> rows = await _uow.Set<PcDocumentDistribution>().AsNoTracking().Where(x => !x.Deleted).OrderBy(x => x.Discipline).ThenBy(x => x.UserId).ToListAsync(ct);
            Dictionary<int, string> names = await NamesAsync(rows.Select(x => (int?)x.UserId), ct);
            return ResponseDtoHelper.Ok(rows.Select(x => new PcDistributionDto { Id = x.Id, Discipline = x.Discipline, UserId = x.UserId, ContractId = x.ContractId, IsActive = x.IsActive, UserName = NameOf(names, x.UserId) }).ToList());
        }

        public async Task<ResponseDto<PcDistributionDto>> SaveDistributionAsync(PcDistributionSaveDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Discipline) == null || dto.UserId <= 0) return Bad<PcDistributionDto>(PcText.InvalidData);
            if (!_user.IsInRole(EdmsRoles.Managers)) return Bad<PcDistributionDto>(PcText.NotAllowed);
            if (!await _uow.Set<Entities.Identity.User>().AnyAsync(u => u.Id == dto.UserId, ct)) return PcText.NotFound<PcDistributionDto>("کاربر پیدا نشد.");
            if (dto.ContractId.HasValue && !await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId.Value && !c.Deleted, ct)) return PcText.NotFound<PcDistributionDto>(PcText.ContractNotFound);
            string disc = Clean(dto.Discipline);
            PcDocumentDistribution x;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                x = await _uow.Set<PcDocumentDistribution>().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, ct);
                if (x == null) return PcText.NotFound<PcDistributionDto>("ردیف پیدا نشد.");
            }
            else
            {
                if (await _uow.Set<PcDocumentDistribution>().AnyAsync(r => !r.Deleted && r.Discipline == disc && r.UserId == dto.UserId && r.ContractId == dto.ContractId, ct)) return Bad<PcDistributionDto>("این شخص قبلاً در فهرست توزیع این دیسیپلین است.");
                x = new PcDocumentDistribution { CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcDocumentDistribution>().Add(x);
            }
            x.Discipline = disc; x.UserId = dto.UserId; x.ContractId = dto.ContractId; x.IsActive = dto.IsActive; x.ModifyDate = now; x.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await GetDistributionAsync(ct)).Data.First(r => r.Id == x.Id));
        }

        public async Task<ResponseDto<List<PcApproverDto>>> GetApproversAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.DocReaders)) return Bad<List<PcApproverDto>>(PcText.NotAllowed);
            List<PcDisciplineApprover> rows = await _uow.Set<PcDisciplineApprover>().AsNoTracking().Where(x => !x.Deleted).OrderBy(x => x.Discipline).ThenBy(x => x.UserId).ToListAsync(ct);
            Dictionary<int, string> names = await NamesAsync(rows.Select(x => (int?)x.UserId), ct);
            return ResponseDtoHelper.Ok(rows.Select(x => new PcApproverDto { Id = x.Id, Discipline = x.Discipline, UserId = x.UserId, ContractId = x.ContractId, CanReview = x.CanReview, CanApprove = x.CanApprove, IsActive = x.IsActive, UserName = NameOf(names, x.UserId) }).ToList());
        }

        public async Task<ResponseDto<PcApproverDto>> SaveApproverAsync(PcApproverSaveDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Discipline) == null || dto.UserId <= 0) return Bad<PcApproverDto>(PcText.InvalidData);
            if (!_user.IsInRole(EdmsRoles.MatrixEditors) || IsAi) return Bad<PcApproverDto>(PcText.NotAllowed + " (ماتریس تایید را فقط PMO ویرایش می‌کند)");
            if (!dto.CanReview && !dto.CanApprove) return Bad<PcApproverDto>("حداقل یکی از دو اختیار بررسی یا تایید را بدهید.");
            if (!await _uow.Set<Entities.Identity.User>().AnyAsync(u => u.Id == dto.UserId, ct)) return PcText.NotFound<PcApproverDto>("کاربر پیدا نشد.");
            if (dto.ContractId.HasValue && !await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId.Value && !c.Deleted, ct)) return PcText.NotFound<PcApproverDto>(PcText.ContractNotFound);
            string disc = Clean(dto.Discipline);
            PcDisciplineApprover x;
            DateTimeOffset now = DateTimeOffset.Now;
            if (dto.Id.HasValue)
            {
                x = await _uow.Set<PcDisciplineApprover>().FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, ct);
                if (x == null) return PcText.NotFound<PcApproverDto>("ردیف پیدا نشد.");
            }
            else
            {
                if (await _uow.Set<PcDisciplineApprover>().AnyAsync(r => !r.Deleted && r.Discipline == disc && r.UserId == dto.UserId && r.ContractId == dto.ContractId, ct)) return Bad<PcApproverDto>("این شخص قبلاً در ماتریس این دیسیپلین است.");
                x = new PcDisciplineApprover { CreateDate = now, CreatorUserId = Me };
                _uow.Set<PcDisciplineApprover>().Add(x);
            }
            x.Discipline = disc; x.UserId = dto.UserId; x.ContractId = dto.ContractId; x.CanReview = dto.CanReview; x.CanApprove = dto.CanApprove; x.IsActive = dto.IsActive; x.ModifyDate = now; x.ModifierUserId = Me;
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok((await GetApproversAsync(ct)).Data.First(r => r.Id == x.Id));
        }

        // ============================================================================ notices

        public async Task<ResponseDto<PagedList<PcNoticeDto>>> GetNoticesAsync(PcDocNoticeQuery query, CancellationToken ct)
        {
            query ??= new PcDocNoticeQuery();
            bool all = !query.MineOnly;
            if (all && !_user.IsInRole(EdmsRoles.Managers)) return Bad<PagedList<PcNoticeDto>>(PcText.NotAllowed);
            IQueryable<PcDocumentNotice> q = _uow.Set<PcDocumentNotice>().AsNoTracking();
            if (!all) q = q.Where(n => n.UserId == Me);
            if (query.OpenOnly) q = q.Where(n => n.AcknowledgedAt == null);
            if (query.DeliverableId.HasValue) q = q.Where(n => n.Revision.DeliverableId == query.DeliverableId.Value);
            PagedList<PcDocumentNotice> page = await PagedList<PcDocumentNotice>.CreateAsync(q.OrderByDescending(n => n.Id), query.PageNumber, query.PageSize, ct);
            List<int> revIds = page.Select(n => n.RevisionId).Distinct().ToList();
            var revs = (await _uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => revIds.Contains(r.Id)).Select(r => new { r.Id, r.Code, r.DeliverableId }).ToListAsync(ct)).ToDictionary(r => r.Id);
            List<int> dIds = revs.Values.Select(r => r.DeliverableId).Distinct().ToList();
            var docs = (await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => dIds.Contains(d.Id)).Select(d => new { d.Id, d.DocumentNumber, d.Title }).ToListAsync(ct)).ToDictionary(d => d.Id);
            Dictionary<int, string> names = await NamesAsync(page.Select(n => (int?)n.UserId), ct);
            int ackDays = (int)(await PcSettingKeys.LoadAsync(_uow, ct))[PcSettingKeys.EdmsNoticeAckDays];
            DateTimeOffset now = DateTimeOffset.Now;
            List<PcNoticeDto> dtos = page.Select(n =>
            {
                var rev = revs[n.RevisionId]; var doc = docs[rev.DeliverableId];
                return new PcNoticeDto
                {
                    Id = n.Id, RevisionId = n.RevisionId, DeliverableId = rev.DeliverableId, DocumentNumber = doc.DocumentNumber, Title = doc.Title, RevisionCode = rev.Code, NotifiedAt = n.NotifiedAt,
                    AcknowledgedAt = n.AcknowledgedAt, UserId = n.UserId, UserName = NameOf(names, n.UserId), Overdue = n.AcknowledgedAt == null && n.NotifiedAt.AddDays(ackDays) < now,
                };
            }).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcNoticeDto>)new PcPage<PcNoticeDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcNoticeDto>> AcknowledgeAsync(int noticeId, CancellationToken ct)
        {
            PcDocumentNotice n = await _uow.Set<PcDocumentNotice>().FirstOrDefaultAsync(x => x.Id == noticeId, ct);
            if (n == null) return PcText.NotFound<PcNoticeDto>("ابلاغ پیدا نشد.");
            if (n.UserId != Me) return Bad<PcNoticeDto>("هر ابلاغ را خود گیرنده تایید می‌کند.");
            if (n.AcknowledgedAt.HasValue) return Bad<PcNoticeDto>("این ابلاغ قبلاً تایید شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            n.AcknowledgedAt = now;
            int deliverableId = await _uow.Set<PcDocumentRevision>().Where(r => r.Id == n.RevisionId).Select(r => r.DeliverableId).FirstAsync(ct);
            Log(deliverableId, n.RevisionId, PcDocEventKind.Acknowledged, null, now);
            await _uow.SaveChangesAsync(ct);
            if (_notify != null) { try { await _notify.CloseAsync("edms-release:" + n.RevisionId + ":" + n.UserId, "دریافت سند تایید شد", ct); } catch (Exception) { /* the inbox never blocks */ } }
            PagedList<PcNoticeDto> page = (await GetNoticesAsync(new PcDocNoticeQuery { PageSize = 200, MineOnly = true }, ct)).Data;
            return ResponseDtoHelper.Ok(page.First(x => x.Id == n.Id));
        }
    }
}
