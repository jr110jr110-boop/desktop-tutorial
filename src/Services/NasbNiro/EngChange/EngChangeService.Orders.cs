using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.EngChangeDtos;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.EngChange
{
    public partial class EngChangeService
    {
        private async Task<EcOrder> LoadOrderAsync(int id, bool tracked, CancellationToken ct)
        {
            IQueryable<EcOrder> q = _uow.Set<EcOrder>().Where(o => o.Id == id && !o.Deleted).Include(o => o.Request).ThenInclude(r => r.Targets).Include(o => o.Changes).Include(o => o.Dispositions).Include(o => o.Tasks);
            return await (tracked ? q : q.AsNoTracking()).FirstOrDefaultAsync(ct);
        }

        // ============================================================================================== list / get

        public async Task<ResponseDto<List<EcOrderListDto>>> GetOrdersAsync(EcOrderQuery query, CancellationToken ct)
        {
            if (!InRole(EcRules.Readers)) return Bad<List<EcOrderListDto>>(NotAllowed);
            query ??= new EcOrderQuery();
            IQueryable<EcOrder> q = _uow.Set<EcOrder>().AsNoTracking().Include(o => o.Request).Include(o => o.Tasks).Where(o => !o.Deleted);
            if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);
            if (query.RequestId.HasValue) q = q.Where(o => o.RequestId == query.RequestId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(o => o.Number.Contains(text) || o.Title.Contains(text));
            string pn = Clean(query.PartNumber)?.ToUpperInvariant();
            if (pn != null) q = q.Where(o => o.AffectedParts.Contains("," + pn + ","));
            List<EcOrder> rows = await q.OrderByDescending(o => o.Id).Take(500).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(ListDto).ToList());
        }

        private static EcOrderListDto ListDto(EcOrder o) => new EcOrderListDto
        {
            Id = o.Id, Number = o.Number, RequestId = o.RequestId, RequestNumber = o.Request?.Number, Title = o.Title, Status = o.Status, StatusText = StatusText(o.Status),
            Effectivity = EcRules.EffectivityText(o), Parts = (o.AffectedParts ?? "").Trim(',').Replace(",", "، "), CreatedAt = o.CreateDate, OpenTasks = o.Tasks.Count(t => !t.Deleted && t.Status != EcTaskStatus.Verified),
        };

        public async Task<ResponseDto<EcOrderDto>> GetOrderAsync(int id, CancellationToken ct)
        {
            if (!InRole(EcRules.Readers)) return Bad<EcOrderDto>(NotAllowed);
            EcOrder o = await LoadOrderAsync(id, false, ct);
            return o == null ? ResponseDtoHelper.NotFound<EcOrderDto>(NotFound) : ResponseDtoHelper.Ok(await OrderDtoAsync(o, ct));
        }

        private async Task<string> ChangeStateAsync(EcOrderChange c, CancellationToken ct)
        {
            switch (c.Kind)
            {
                case EcChangeKind.Bom:
                {
                    PbBomStatus? s = await _uow.Set<PbBomHeader>().AsNoTracking().Where(h => h.Id == c.TargetId).Select(h => (PbBomStatus?)h.Status).FirstOrDefaultAsync(ct);
                    return s == null ? "؟" : EcImpactBuilder.StatusText(s.Value);
                }
                case EcChangeKind.Opc:
                {
                    PbRoutingStatus? s = await _uow.Set<PbOpcRouting>().AsNoTracking().Where(h => h.Id == c.TargetId).Select(h => (PbRoutingStatus?)h.Status).FirstOrDefaultAsync(ct);
                    return s == null ? "؟" : s.Value.ToString();
                }
                case EcChangeKind.Drawing:
                {
                    PcDocStatus? s = await _uow.Set<PcDocumentRevision>().AsNoTracking().Where(h => h.Id == c.TargetId).Select(h => (PcDocStatus?)h.Status).FirstOrDefaultAsync(ct);
                    return s == null ? "؟" : s.Value.ToString();
                }
                default:
                {
                    MfRoutingStatus? s = await _uow.Set<MfRoutingRevision>().AsNoTracking().Where(h => h.Id == c.TargetId).Select(h => (MfRoutingStatus?)h.Status).FirstOrDefaultAsync(ct);
                    return s == null ? "؟" : s.Value.ToString();
                }
            }
        }

        /// <summary>Is the named revision in the state the order waits for (released / approved, or obsolete for an Obsolete action)?</summary>
        private async Task<bool> ChangeDoneAsync(EcOrderChange c, CancellationToken ct)
        {
            if (c.Action == EcChangeAction.Obsolete)
                return await _uow.Set<PbBomHeader>().AsNoTracking().AnyAsync(h => h.Id == c.TargetId && h.Status == PbBomStatus.Obsolete, ct);
            switch (c.Kind)
            {
                case EcChangeKind.Bom: return await _uow.Set<PbBomHeader>().AsNoTracking().AnyAsync(h => h.Id == c.TargetId && (h.Status == PbBomStatus.Released || h.Status == PbBomStatus.Superseded || h.Status == PbBomStatus.Obsolete), ct);
                case EcChangeKind.Opc: return await _uow.Set<PbOpcRouting>().AsNoTracking().AnyAsync(h => h.Id == c.TargetId && (h.Status == PbRoutingStatus.Approved || h.Status == PbRoutingStatus.Superseded), ct);
                case EcChangeKind.Drawing: return await _uow.Set<PcDocumentRevision>().AsNoTracking().AnyAsync(h => h.Id == c.TargetId && (h.Status == PcDocStatus.Released || h.Status == PcDocStatus.AsBuilt || h.Status == PcDocStatus.Superseded), ct);
                case EcChangeKind.CharList: return await _uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().AsNoTracking().AnyAsync(h => h.Id == c.TargetId && (h.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Released || h.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Superseded), ct);
                default: return await _uow.Set<MfRoutingRevision>().AsNoTracking().AnyAsync(h => h.Id == c.TargetId && h.Status == MfRoutingStatus.Approved, ct);
            }
        }

        private static string Hash(EcOrder o)
        {
            var sb = new StringBuilder();
            sb.Append("T:").Append(o.Title).Append("|S:").Append(o.Summary).Append("|E:").Append(o.EffectiveFromOrderNumber).Append('|').Append(o.EffectiveFromSerial).Append('|').Append(o.EffectiveFromDate?.ToString("yyyy-MM-dd")).Append('|').Append(o.EffectivityNote);
            sb.Append("|I:").Append(o.ImpactSha256);
            foreach (EcOrderChange c in o.Changes.OrderBy(c => c.Kind).ThenBy(c => c.TargetId).ThenBy(c => c.Action)) sb.Append("|C:").Append((int)c.Kind).Append(':').Append((int)c.Action).Append(':').Append(c.TargetId).Append(':').Append(c.Note);
            foreach (EcDispositionItem d in o.Dispositions.Where(d => !d.Stale).OrderBy(d => d.ImpactKey, StringComparer.Ordinal))
                sb.Append("|D:").Append(d.ImpactKey).Append(':').Append(d.Disposition.HasValue ? (int)d.Disposition.Value : 0).Append(':').Append(d.Note).Append(':').Append(d.OwnerUserId).Append(':').Append(d.DueDate?.ToString("yyyy-MM-dd"));
            return EcRules.Sha256(sb.ToString());
        }

        /// <summary>
        /// The fingerprint the approvals are bound to: the order itself plus the CONTENT of every named BOM / OPC revision (read from its lines, so it does not depend on the review state). A named draft revision cannot be edited
        /// while the order is in approval or approved (see EcGate.NamedInOpenOrderAsync), and an edit before the approval changes this fingerprint.
        /// </summary>
        private async Task<string> HashAsync(EcOrder o, CancellationToken ct)
        {
            var sb = new StringBuilder(Hash(o));
            foreach (EcOrderChange c in o.Changes.OrderBy(c => c.Kind).ThenBy(c => c.TargetId).ThenBy(c => c.Action))
            {
                if (c.Action != EcChangeAction.NewRevision) continue;
                string digest = null;
                if (c.Kind == EcChangeKind.Bom)
                {
                    var lines = await _uow.Set<PbBomLine>().AsNoTracking().Where(l => l.BomHeaderId == c.TargetId && !l.Deleted).OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                        .Select(l => new { l.PartNumber, l.Qty, l.Unit, l.IsHeading, l.MakeOrBuy, l.ChildBomId, l.AlternativeOfLineId, l.ScrapFactor }).ToListAsync(ct);
                    digest = EcRules.Sha256(string.Join("\n", lines.Select(l => $"{l.PartNumber}|{l.Qty:0.####}|{l.Unit}|{(l.IsHeading ? 1 : 0)}|{(int)l.MakeOrBuy}|{l.ChildBomId}|{l.AlternativeOfLineId}|{l.ScrapFactor:0.####}")));
                }
                else if (c.Kind == EcChangeKind.Opc)
                {
                    var steps = await _uow.Set<PbOpcStep>().AsNoTracking().Where(l => l.RoutingId == c.TargetId && !l.Deleted).OrderBy(l => l.PartNumber).ThenBy(l => l.Seq).ThenBy(l => l.Id)
                        .Select(l => new { l.PartNumber, l.Seq, l.StageCode2, l.Title, l.StandardHours, l.IsQcPoint, l.WorkCenterId }).ToListAsync(ct);
                    digest = EcRules.Sha256(string.Join("\n", steps.Select(l => $"{l.PartNumber}|{l.Seq}|{l.StageCode2}|{l.Title}|{l.StandardHours:0.####}|{(l.IsQcPoint ? 1 : 0)}|{l.WorkCenterId}")));
                }
                if (digest != null) sb.Append("|R:").Append((int)c.Kind).Append(':').Append(c.TargetId).Append(':').Append(digest);
            }
            return EcRules.Sha256(sb.ToString());
        }

        private static List<EcImpactItemDto> LiveItems(EcImpactBuilder.Snapshot s) => s?.Items ?? new List<EcImpactItemDto>();

        /// <summary>What stops the submission or the approval. live = recompute the impact now and compare it with the stored one.</summary>
        private async Task<List<string>> BlockersAsync(EcOrder o, bool live, CancellationToken ct)
        {
            var list = new List<string>();
            if (!o.Changes.Any()) list.Add("هیچ بازنگری (BOM، OPC، نقشه یا مسیر ساخت) در دستور تغییر نام برده نشده است.");
            if (!EcRules.Stated(o)) list.Add("تاریخ/نقطه اثر (شماره دستور تولید، شماره سریال یا تاریخ) تعیین نشده است.");
            EcImpactBuilder.Snapshot stored = EcImpactBuilder.Parse(o.ImpactJson);
            if (stored == null) { list.Add("تحلیل اثر هنوز محاسبه نشده است."); return list; }
            if (EcRules.Sha256(o.ImpactJson) != o.ImpactSha256) list.Add("اثر انگشت تحلیل اثر با محتوای آن نمی‌خواند؛ تحلیل را دوباره محاسبه کنید.");
            if (stored.Truncated.Count > 0) list.Add("فهرست‌های تحلیل اثر بیش از حد بلند بودند و بریده شده‌اند؛ دامنه درخواست را کوچک‌تر کنید.");
            var active = o.Dispositions.Where(d => !d.Stale).ToList();
            int undecided = active.Count(d => !d.Disposition.HasValue);
            if (undecided > 0) list.Add($"{undecided} مورد اثر‌پذیر هنوز تکلیف (disposition) ندارد.");
            foreach (EcDispositionItem d in active.Where(d => d.Disposition.HasValue))
            {
                if (!EcRules.IsAllowed(d.Category, d.Disposition.Value)) list.Add($"تکلیف «{EcRules.Title(d.Disposition.Value)}» برای «{d.Text}» مجاز نیست.");
                else if (EcRules.NeedsNote(d.Disposition.Value) && Clean(d.Note) == null) list.Add($"برای «{d.Text}» با تکلیف «{EcRules.Title(d.Disposition.Value)}» توضیح لازم است.");
                if (EcRules.NeedsTask(d.Disposition.Value) && (!d.OwnerUserId.HasValue || !d.DueDate.HasValue)) list.Add($"برای «{d.Text}» مسئول و مهلت اجرا لازم است.");
            }
            if (live)
            {
                EcImpactBuilder.Snapshot now = await ComputeImpactAsync(o, ct);
                HashSet<string> have = active.Select(d => d.ImpactKey).ToHashSet();
                List<string> missing = LiveItems(now).Select(i => i.Key).Where(k => !have.Contains(k)).ToList();
                if (missing.Count > 0) list.Add($"از زمان آخرین تحلیل {missing.Count} مورد اثر‌پذیر تازه پیدا شده است؛ تحلیل اثر را تازه کنید و برای آن‌ها تکلیف بگذارید.");
            }
            return list;
        }

        /// <summary>The part numbers whose line is added, removed or changed in quantity / unit between two BOM revisions.</summary>
        private async Task<List<string>> BomDiffPartsAsync(int oldHeaderId, int newHeaderId, CancellationToken ct)
        {
            async Task<Dictionary<string, string>> Lines(int id) => (await _uow.Set<PbBomLine>().AsNoTracking().Where(l => l.BomHeaderId == id && !l.Deleted && !l.IsHeading && l.AlternativeOfLineId == null && l.PartNumber != null).ToListAsync(ct))
                .GroupBy(l => l.PartNumber.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.Sum(l => l.Qty).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "|" + string.Join(",", g.Select(l => l.Unit).Distinct().OrderBy(u => u)));
            Dictionary<string, string> a = await Lines(oldHeaderId), b = await Lines(newHeaderId);
            return a.Keys.Union(b.Keys).Where(k => !a.TryGetValue(k, out string x) || !b.TryGetValue(k, out string y) || x != y).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        private async Task<EcImpactBuilder.Snapshot> ComputeImpactAsync(EcOrder o, CancellationToken ct)
        {
            var input = new EcImpactInput();
            foreach (EcRequestTarget t in o.Request.Targets)
            {
                if (!string.IsNullOrWhiteSpace(t.PartNumber)) input.PartNumbers.Add(t.PartNumber);
                if (t.BomHeaderId.HasValue) input.BomHeaderIds.Add(t.BomHeaderId.Value);
                if (t.OpcRoutingId.HasValue) input.OpcRoutingIds.Add(t.OpcRoutingId.Value);
                if (t.DeliverableId.HasValue) input.DeliverableIds.Add(t.DeliverableId.Value);
            }
            foreach (EcOrderChange c in o.Changes)
            {
                if (!string.IsNullOrWhiteSpace(c.PartNumber)) input.PartNumbers.Add(c.PartNumber);
                if (c.Kind == EcChangeKind.Bom)
                {
                    input.BomHeaderIds.Add(c.TargetId); if (c.OldTargetId.HasValue) input.BomHeaderIds.Add(c.OldTargetId.Value);
                    // the components the new revision adds, removes or changes against the revision in force are impacted too (stock, orders, purchases of THOSE parts)
                    if (c.OldTargetId.HasValue && c.Action == EcChangeAction.NewRevision) input.PartNumbers.AddRange(await BomDiffPartsAsync(c.OldTargetId.Value, c.TargetId, ct));
                }
                if (c.Kind == EcChangeKind.Opc) { input.OpcRoutingIds.Add(c.TargetId); if (c.OldTargetId.HasValue) input.OpcRoutingIds.Add(c.OldTargetId.Value); }
                if (c.Kind == EcChangeKind.CharList)
                {
                    int? lid = await _uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().AsNoTracking().Where(r => r.Id == c.TargetId).Select(r => (int?)r.DeliverableId).FirstOrDefaultAsync(ct);
                    if (lid.HasValue) input.DeliverableIds.Add(lid.Value);
                }
                if (c.Kind == EcChangeKind.Drawing)
                {
                    int? did = await _uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => r.Id == c.TargetId).Select(r => (int?)r.DeliverableId).FirstOrDefaultAsync(ct);
                    if (did.HasValue) input.DeliverableIds.Add(did.Value);
                }
            }
            return await EcImpactBuilder.ComputeAsync(_uow, input, ct);
        }

        private async Task<EcOrderDto> OrderDtoAsync(EcOrder o, CancellationToken ct)
        {
            var ids = new List<int?> { o.CreatorUserId, o.EngineeringApprovedByUserId, o.QaApprovedByUserId };
            ids.AddRange(o.Dispositions.Select(d => d.OwnerUserId));
            ids.AddRange(o.Tasks.SelectMany(t => new int?[] { t.OwnerUserId, t.DoneByUserId, t.VerifiedByUserId }));
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, ids, ct);
            EcImpactBuilder.Snapshot snap = EcImpactBuilder.Parse(o.ImpactJson);
            var active = o.Dispositions.Where(d => !d.Stale).ToList();
            var dto = new EcOrderDto
            {
                Id = o.Id, Number = o.Number, RequestId = o.RequestId, RequestNumber = o.Request?.Number, Title = o.Title, Status = o.Status, StatusText = StatusText(o.Status),
                Effectivity = EcRules.EffectivityText(o), Parts = (o.AffectedParts ?? "").Trim(',').Replace(",", "، "), CreatedAt = o.CreateDate, OpenTasks = o.Tasks.Count(t => !t.Deleted && t.Status != EcTaskStatus.Verified),
                Summary = o.Summary, Classification = o.Request.Classification, Urgency = o.Request.Urgency, RequestTitle = o.Request.Title, RequestReason = o.Request.Reason,
                EffectiveFromOrderNumber = o.EffectiveFromOrderNumber, EffectiveFromSerial = o.EffectiveFromSerial, EffectiveFromDate = o.EffectiveFromDate, EffectivityNote = o.EffectivityNote,
                CreatorUserId = o.CreatorUserId, CreatorName = names.Name(o.CreatorUserId), SubmittedAt = o.SubmittedAt,
                EngineeringApprovedByName = names.Name(o.EngineeringApprovedByUserId), EngineeringApprovedAt = o.EngineeringApprovedAt, EngineeringNote = o.EngineeringNote,
                QaApprovedByName = names.Name(o.QaApprovedByUserId), QaApprovedAt = o.QaApprovedAt, QaNote = o.QaNote, ApprovedAt = o.ApprovedAt, RejectReason = o.RejectReason, CancelReason = o.CancelReason,
                ClosedAt = o.ClosedAt, ContentSha256 = o.ContentSha256, RowVersion = o.RowVersion == null ? null : Convert.ToBase64String(o.RowVersion),
            };
            if (snap != null)
            {
                dto.Impact = new EcImpactDto { ComputedAt = o.ImpactAt, Parts = snap.Parts, Items = snap.Items, Sha256 = o.ImpactSha256, HashOk = EcRules.Sha256(o.ImpactJson) == o.ImpactSha256 };
                foreach (EcImpactCategory cat in Enum.GetValues(typeof(EcImpactCategory)).Cast<EcImpactCategory>())
                    dto.Impact.Counts.Add(new EcImpactCountDto { Category = cat, Title = EcRules.Title(cat), Count = snap.Items.Count(i => i.Category == cat), Decided = active.Count(d => d.Category == cat && d.Disposition.HasValue), Truncated = snap.Truncated.Contains((int)cat) });
            }
            foreach (EcOrderChange c in o.Changes.OrderBy(c => c.Id))
                dto.Changes.Add(new EcChangeDto { Id = c.Id, Kind = c.Kind, Action = c.Action, TargetId = c.TargetId, OldTargetId = c.OldTargetId, Code = c.Code, PartNumber = c.PartNumber, OldRevision = c.OldRevision, NewRevision = c.NewRevision, Reference = c.Reference, Note = c.Note, State = await ChangeStateAsync(c, ct) });
            foreach (EcDispositionItem d in o.Dispositions.OrderBy(d => (int)d.Category).ThenBy(d => d.ImpactKey, StringComparer.Ordinal))
                dto.Dispositions.Add(new EcDispositionDto
                {
                    Id = d.Id, Category = d.Category, CategoryTitle = EcRules.Title(d.Category), ImpactKey = d.ImpactKey, Text = d.Text, PartNumber = d.PartNumber, Quantity = d.Quantity, Disposition = d.Disposition,
                    DispositionTitle = d.Disposition.HasValue ? EcRules.Title(d.Disposition.Value) : null, Note = d.Note, OwnerUserId = d.OwnerUserId, OwnerName = names.Name(d.OwnerUserId), DueDate = d.DueDate, Stale = d.Stale,
                    Allowed = EcRules.Allowed(d.Category).ToList(),
                });
            foreach (EcTask t in o.Tasks.Where(t => !t.Deleted).OrderBy(t => t.Id))
                dto.Tasks.Add(new EcTaskDto
                {
                    Id = t.Id, DispositionItemId = t.DispositionItemId, Title = t.Title, Disposition = t.Disposition, DispositionTitle = EcRules.Title(t.Disposition), OwnerUserId = t.OwnerUserId, OwnerName = names.Name(t.OwnerUserId),
                    DueDate = t.DueDate, Status = t.Status, StatusText = t.Status == EcTaskStatus.Open ? "باز" : t.Status == EcTaskStatus.Done ? "انجام شده، منتظر تایید QA" : "تایید شده",
                    Overdue = t.Status == EcTaskStatus.Open && t.DueDate.Date < DateTime.Today, DoneAt = t.DoneAt, DoneByName = names.Name(t.DoneByUserId), DoneNote = t.DoneNote,
                    VerifiedAt = t.VerifiedAt, VerifiedByName = names.Name(t.VerifiedByUserId), VerifyNote = t.VerifyNote,
                    CanDone = !IsAi && o.Status == EcoStatus.Approved && t.Status == EcTaskStatus.Open && (t.OwnerUserId == Me || InRole(EcRules.RoleEngineering)),
                    CanVerify = !IsAi && o.Status == EcoStatus.Approved && t.Status == EcTaskStatus.Done && InRole(EcRules.RoleQa) && t.DoneByUserId != Me,
                });
            bool human = !IsAi;
            bool draft = o.Status == EcoStatus.Draft;
            dto.CanEdit = human && draft && InRole(EcRules.OrderMakers);
            dto.CanSubmit = dto.CanEdit;
            if (o.Status == EcoStatus.Draft || o.Status == EcoStatus.InApproval) dto.Blockers = await BlockersAsync(o, false, ct);
            bool inApproval = o.Status == EcoStatus.InApproval;
            dto.CanApproveEngineering = human && inApproval && o.EngineeringApprovedByUserId == null && InRole(EcRules.RoleEngineering) && ApproveProblem(o, Me, o.QaApprovedByUserId) == null;
            dto.CanApproveQa = human && inApproval && o.QaApprovedByUserId == null && InRole(EcRules.RoleQa) && ApproveProblem(o, Me, o.EngineeringApprovedByUserId) == null;
            dto.CanReject = human && inApproval && (InRole(EcRules.RoleEngineering) || InRole(EcRules.RoleQa)) && ApproveProblem(o, Me, null) == null;
            dto.CanCancel = human && (draft || inApproval) && (o.CreatorUserId == Me || InRole(EcRules.RoleEngineering));
            dto.CanClose = human && o.Status == EcoStatus.Approved && (InRole(EcRules.RoleEngineering) || InRole(EcRules.RoleQa));
            dto.CanVoid = dto.CanClose;
            return dto;
        }

        private string ApproveProblem(EcOrder o, int userId, int? otherApproverId)
        {
            if (o.Request != null && o.Request.RequestedByUserId == userId) return "درخواست‌دهنده تغییر آن را تایید نمی‌کند.";
            return EcRules.ApproverProblem(userId, o.CreatorUserId, o.EditorUserIds, otherApproverId);
        }

        // ============================================================================================== save

        private string CheckEditable(EcOrder o, bool forApprovers = false)
        {
            if (IsAi) return AiNeverApproves;
            if (!InRole(EcRules.OrderMakers)) return NotAllowed;
            if (o.Status != EcoStatus.Draft) return "فقط دستور پیش‌نویس ویرایش می‌شود.";
            return null;
        }

        private void Touch(EcOrder o)
        {
            o.EditorUserIds = EcRules.AddEditor(o.EditorUserIds, Me);
            Stamp(o, false, Me);
        }

        public async Task<ResponseDto<EcOrderDto>> SaveOrderAsync(SaveEcOrderDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<EcOrderDto>(AiNeverApproves);
            if (!InRole(EcRules.OrderMakers)) return Bad<EcOrderDto>(NotAllowed);
            if (dto == null) return Bad<EcOrderDto>("اطلاعات دستور تغییر خالی است.");
            EcOrder o;
            if (dto.Id.HasValue)
            {
                o = await LoadOrderAsync(dto.Id.Value, true, ct);
                if (o == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
                string e = CheckEditable(o);
                if (e != null) return Bad<EcOrderDto>(e);
            }
            else
            {
                EcRequest r = dto.RequestId.HasValue ? await _uow.Set<EcRequest>().Include(x => x.Targets).FirstOrDefaultAsync(x => x.Id == dto.RequestId.Value && !x.Deleted, ct) : null;
                if (r == null) return Bad<EcOrderDto>("درخواست تغییر پیدا نشد.");
                if (r.Status != EcrStatus.Approved) return Bad<EcOrderDto>("دستور تغییر فقط از درخواست تاییدشده ساخته می‌شود.");
                if (await _uow.Set<EcOrder>().AnyAsync(x => !x.Deleted && x.RequestId == r.Id && x.Status != EcoStatus.Rejected && x.Status != EcoStatus.Cancelled, ct)) return Bad<EcOrderDto>("برای این درخواست دستور تغییر فعالی وجود دارد.");
                o = new EcOrder { RequestId = r.Id, Request = r, Status = EcoStatus.Draft, Title = r.Title, Number = await InventoryNumbers.NextNumberAsync(_uow, EcNumbers.Order, "EC-ECO", DateTime.Today, ct) };
                Stamp(o, true, Me);
                _uow.Set<EcOrder>().Add(o);
            }
            if (Clean(dto.Title) != null) o.Title = dto.Title.Trim();
            o.Summary = Clean(dto.Summary);
            o.EffectiveFromOrderNumber = Clean(dto.EffectiveFromOrderNumber);
            o.EffectiveFromSerial = Clean(dto.EffectiveFromSerial);
            o.EffectiveFromDate = dto.EffectiveFromDate?.Date;
            o.EffectivityNote = Clean(dto.EffectivityNote);
            o.ContentSha256 = null;
            Touch(o);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcOrderDto>(conflict);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(await LoadOrderAsync(o.Id, false, ct), ct));
        }

        // ============================================================================================== changes

        public async Task<ResponseDto<EcOrderDto>> AddChangeAsync(EcOrderChangeSaveDto dto, CancellationToken ct)
        {
            EcOrder o = dto == null ? null : await LoadOrderAsync(dto.OrderId, true, ct);
            if (o == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
            string e = CheckEditable(o);
            if (e != null) return Bad<EcOrderDto>(e);
            if (o.Changes.Any(c => c.Kind == dto.Kind && c.TargetId == dto.TargetId && c.Action == dto.Action)) return Bad<EcOrderDto>("این بازنگری قبلاً در دستور نام برده شده است.");
            var c = new EcOrderChange { OrderId = o.Id, Kind = dto.Kind, Action = dto.Action, TargetId = dto.TargetId, Note = Clean(dto.Note) };
            string problem = await FillChangeAsync(c, ct);
            if (problem != null) return Bad<EcOrderDto>(problem);
            o.Changes.Add(c);
            await RecomputePartsAsync(o, ct);
            o.ContentSha256 = null;
            Touch(o);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcOrderDto>(conflict);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(await LoadOrderAsync(o.Id, false, ct), ct));
        }

        /// <summary>Checks the named revision against its register and fills the code, part, revision numbers and the previous revision.</summary>
        private async Task<string> FillChangeAsync(EcOrderChange c, CancellationToken ct)
        {
            if (c.Action == EcChangeAction.Obsolete && c.Kind != EcChangeKind.Bom) return "فقط BOM با دستور تغییر منسوخ می‌شود.";
            switch (c.Kind)
            {
                case EcChangeKind.Bom:
                {
                    PbBomHeader h = await _uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.TargetId && !x.Deleted, ct);
                    if (h == null) return "BOM پیدا نشد.";
                    c.Code = h.Code; c.PartNumber = h.AssemblyPartNumber; c.NewRevision = h.Revision; c.Reference = $"BOM {h.Code} بازنگری {h.Revision:00}";
                    if (c.Action == EcChangeAction.Obsolete)
                    {
                        if (h.Status != PbBomStatus.Released && h.Status != PbBomStatus.Superseded) return "فقط BOM منتشرشده یا جایگزین‌شده منسوخ می‌شود.";
                        return null;
                    }
                    if (h.Status != PbBomStatus.Draft && h.Status != PbBomStatus.UnderReview && h.Status != PbBomStatus.Approved) return "بازنگری جدید باید هنوز منتشر نشده باشد (پیش‌نویس، در بررسی یا تاییدشده).";
                    PbBomHeader old = await _uow.Set<PbBomHeader>().AsNoTracking().Where(x => !x.Deleted && x.Code == h.Code && x.Revision < h.Revision && (x.Status == PbBomStatus.Released || x.Status == PbBomStatus.Superseded)).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
                    if (old == null) return "برای بازنگری اول BOM دستور تغییر لازم نیست؛ بازنگری قبلی منتشرشده‌ای وجود ندارد.";
                    c.OldTargetId = old.Id; c.OldRevision = old.Revision;
                    return null;
                }
                case EcChangeKind.Opc:
                {
                    PbOpcRouting r = await _uow.Set<PbOpcRouting>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.TargetId && !x.Deleted, ct);
                    if (r == null) return "چارت OPC پیدا نشد.";
                    c.Code = r.PartNumber; c.PartNumber = r.PartNumber; c.NewRevision = r.Revision; c.Reference = $"OPC {r.PartNumber} بازنگری {r.Revision:00}";
                    if (r.Status == PbRoutingStatus.Approved || r.Status == PbRoutingStatus.Superseded) return "بازنگری جدید باید هنوز تایید نشده باشد.";
                    PbOpcRouting old = await _uow.Set<PbOpcRouting>().AsNoTracking().Where(x => !x.Deleted && x.PartNumber == r.PartNumber && x.Revision < r.Revision && (x.Status == PbRoutingStatus.Approved || x.Status == PbRoutingStatus.Superseded)).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
                    if (old == null) return "برای بازنگری اول چارت OPC دستور تغییر لازم نیست.";
                    c.OldTargetId = old.Id; c.OldRevision = old.Revision;
                    return null;
                }
                case EcChangeKind.Drawing:
                {
                    PcDocumentRevision r = await _uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.TargetId && !x.Deleted, ct);
                    if (r == null) return "بازنگری نقشه پیدا نشد.";
                    PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().FirstAsync(x => x.Id == r.DeliverableId, ct);
                    c.Code = d.DocumentNumber; c.NewRevision = r.Sequence; c.Reference = $"{d.DocumentNumber} بازنگری {r.Code}";
                    if (r.Status == PcDocStatus.Released || r.Status == PcDocStatus.AsBuilt || r.Status == PcDocStatus.Superseded || r.Status == PcDocStatus.Obsolete) return "بازنگری جدید باید هنوز منتشر نشده باشد.";
                    // the drawing is a revision of something that was ever in force when ANY document linked (now or earlier) to its parts has a released, superseded or obsolete revision
                    PcDocumentRevision old = await EcGate.EarlierDrawingRevisionAsync(_uow, d, r, ct);
                    var (parts, _) = await EcGate.DrawingPartsAsync(_uow, d.Id, true, ct);
                    if (parts.Count == 0) return "این نقشه به هیچ قطعه‌ای وصل نیست؛ دستور تغییر فقط برای نقشه قطعه‌ها لازم است.";
                    if (old == null) return "برای بازنگری اول نقشه دستور تغییر لازم نیست.";
                    c.OldTargetId = old.Id; c.OldRevision = old.Sequence;
                    c.PartNumber = parts[0];   // every linked part goes into the affected parts of the order (RecomputePartsAsync)
                    return null;
                }
                case EcChangeKind.CharList:
                {
                    var l = await _uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.TargetId && !x.Deleted, ct);
                    if (l == null) return "فهرست مشخصه‌ها پیدا نشد.";
                    c.Code = l.DocumentNumber; c.NewRevision = l.Version; c.Reference = $"فهرست مشخصه‌های {l.DocumentNumber} بازنگری {l.RevisionCode} نسخه {l.Version}";
                    if (l.Status != Entities.Model.NasbNiro.Quality.CharListStatus.Draft && l.Status != Entities.Model.NasbNiro.Quality.CharListStatus.Submitted) return "نسخه جدید فهرست باید هنوز تایید نشده باشد (پیش‌نویس یا ارسال‌شده).";
                    var oldList = await _uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().AsNoTracking().Where(x => !x.Deleted && x.RevisionId == l.RevisionId && x.Version < l.Version
                        && (x.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Released || x.Status == Entities.Model.NasbNiro.Quality.CharListStatus.Superseded)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
                    if (oldList == null) return "برای نسخه اول فهرست مشخصه‌های یک بازنگری دستور تغییر لازم نیست.";
                    c.OldTargetId = oldList.Id; c.OldRevision = oldList.Version;
                    var (parts, _) = await EcGate.DrawingPartsAsync(_uow, l.DeliverableId, true, ct);
                    c.PartNumber = parts.Count == 0 ? null : parts[0];
                    return null;
                }
                default:
                {
                    MfRoutingRevision r = await _uow.Set<MfRoutingRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.TargetId && !x.Deleted, ct);
                    if (r == null) return "بازنگری مسیر ساخت پیدا نشد.";
                    InventoryItem item = r.ItemId.HasValue ? await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == r.ItemId.Value, ct) : null;
                    string groupTitle = r.ItemGroupId.HasValue && !r.ItemId.HasValue ? await _uow.Set<ItemGroup>().AsNoTracking().Where(g => g.Id == r.ItemGroupId.Value).Select(g => g.Title).FirstOrDefaultAsync(ct) : null;
                    c.Code = item?.Code ?? ("گروه " + groupTitle); c.PartNumber = item?.PartNumber; c.NewRevision = r.Revision; c.Reference = $"مسیر ساخت {item?.Code ?? ("گروه " + groupTitle)} بازنگری {r.Revision}";
                    if (r.Status == MfRoutingStatus.Approved) return "بازنگری جدید باید هنوز تایید نشده باشد.";
                    IQueryable<MfRoutingRevision> sameKey = _uow.Set<MfRoutingRevision>().AsNoTracking().Where(x => !x.Deleted && x.Revision < r.Revision && x.Status == MfRoutingStatus.Approved);
                    sameKey = r.ItemId.HasValue ? sameKey.Where(x => x.ItemId == r.ItemId) : sameKey.Where(x => x.ItemGroupId == r.ItemGroupId && x.ItemId == null);
                    MfRoutingRevision old = await sameKey.OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
                    if (old == null) return "برای بازنگری اول مسیر ساخت دستور تغییر لازم نیست.";
                    c.OldTargetId = old.Id; c.OldRevision = old.Revision;
                    return null;
                }
            }
        }

        /// <summary>
        /// The parts the order changes: the parts of the request, of the named revisions (every part a drawing is linked to) and every assembly that contains one of them at any level (the where-used chain), so that the
        /// production orders of a parent product are judged against the effectivity too.
        /// </summary>
        private async Task RecomputePartsAsync(EcOrder o, CancellationToken ct)
        {
            var parts = new List<string>();
            parts.AddRange(o.Request.Targets.Where(t => !string.IsNullOrWhiteSpace(t.PartNumber)).Select(t => t.PartNumber));
            parts.AddRange(o.Changes.Where(c => !string.IsNullOrWhiteSpace(c.PartNumber)).Select(c => c.PartNumber));
            foreach (EcOrderChange c in o.Changes.Where(c => c.Kind == EcChangeKind.Drawing || c.Kind == EcChangeKind.CharList))
            {
                int? did = c.Kind == EcChangeKind.Drawing
                    ? await _uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => r.Id == c.TargetId).Select(r => (int?)r.DeliverableId).FirstOrDefaultAsync(ct)
                    : await _uow.Set<Entities.Model.NasbNiro.Quality.QcCharacteristicList>().AsNoTracking().Where(r => r.Id == c.TargetId).Select(r => (int?)r.DeliverableId).FirstOrDefaultAsync(ct);
                if (did.HasValue) parts.AddRange((await EcGate.DrawingPartsAsync(_uow, did.Value, true, ct)).Parts);
            }
            var seen = new HashSet<string>(parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
            List<string> frontier = seen.ToList();
            for (int level = 1; level <= 8 && frontier.Count > 0 && seen.Count < 80; level++)
            {
                List<string> current = frontier;
                List<string> up = await (from l in _uow.Set<PbBomLine>().AsNoTracking()
                                         join h in _uow.Set<PbBomHeader>().AsNoTracking() on l.BomHeaderId equals h.Id
                                         where !l.Deleted && !h.Deleted && !l.IsHeading && h.Status == PbBomStatus.Released && l.PartNumber != null && current.Contains(l.PartNumber) && h.AssemblyPartNumber != null
                                         select h.AssemblyPartNumber).Distinct().ToListAsync(ct);
                frontier = new List<string>();
                foreach (string p in up.Select(p => p.Trim().ToUpperInvariant()).OrderBy(p => p, StringComparer.Ordinal))
                    if (seen.Count < 80 && seen.Add(p)) frontier.Add(p);
            }
            o.AffectedParts = EcRules.PartsKey(seen);
        }

        public async Task<ResponseDto<EcOrderDto>> RemoveChangeAsync(EcOrderChangeRemoveDto dto, CancellationToken ct)
        {
            EcOrder o = dto == null ? null : await LoadOrderAsync(dto.OrderId, true, ct);
            if (o == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
            string e = CheckEditable(o);
            if (e != null) return Bad<EcOrderDto>(e);
            EcOrderChange c = o.Changes.FirstOrDefault(x => x.Id == dto.ChangeId);
            if (c == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
            o.Changes.Remove(c);
            await RecomputePartsAsync(o, ct);
            o.ContentSha256 = null;
            Touch(o);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcOrderDto>(conflict);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(await LoadOrderAsync(o.Id, false, ct), ct));
        }

        /// <summary>Makes the new draft revision (copy of the one in force) through the BOM / OPC service and names it in the order.</summary>
        public async Task<ResponseDto<EcOrderDto>> NewRevisionAsync(EcNewRevisionDto dto, CancellationToken ct)
        {
            EcOrder o = dto == null ? null : await LoadOrderAsync(dto.OrderId, false, ct);
            if (o == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
            string e = CheckEditable(o);
            if (e != null) return Bad<EcOrderDto>(e);
            string note = Clean(dto.Note) ?? Clean(o.Request.Reason);
            int newId;
            if (dto.Kind == EcChangeKind.Bom)
            {
                if (_bom == null) return Bad<EcOrderDto>("سرویس BOM در دسترس نیست.");
                ResponseDto<PbBomDto> r = await _bom.NewRevisionAsync(new PbBomActionDto { Id = dto.SourceId, Note = $"{o.Number}: {note}" }, ct);
                if (!r.Success) return Bad<EcOrderDto>(r.Message);
                newId = r.Data.Id;
            }
            else if (dto.Kind == EcChangeKind.Opc)
            {
                if (_opc == null) return Bad<EcOrderDto>("سرویس OPC در دسترس نیست.");
                ResponseDto<PbOpcDto> r = await _opc.NewRevisionAsync(new PbOpcActionDto { Id = dto.SourceId, Note = $"{o.Number}: {note}" }, ct);
                if (!r.Success) return Bad<EcOrderDto>(r.Message);
                newId = r.Data.Id;
            }
            else return Bad<EcOrderDto>("نسخه جدید فقط از BOM یا چارت OPC ساخته می‌شود؛ نقشه و مسیر ساخت از ماژول خودشان ساخته و اینجا نام برده می‌شوند.");
            return await AddChangeAsync(new EcOrderChangeSaveDto { OrderId = o.Id, Kind = dto.Kind, Action = EcChangeAction.NewRevision, TargetId = newId, Note = dto.Note }, ct);
        }

        // ============================================================================================== impact and dispositions

        private async Task RefreshImpactCoreAsync(EcOrder o, CancellationToken ct)
        {
            EcImpactBuilder.Snapshot snap = await ComputeImpactAsync(o, ct);
            o.ImpactJson = EcImpactBuilder.Serialize(snap);
            o.ImpactSha256 = EcRules.Sha256(o.ImpactJson);
            o.ImpactAt = snap.ComputedAt;
            o.ImpactByUserId = Me;
            var keys = snap.Items.ToDictionary(i => i.Key, i => i);
            foreach (EcDispositionItem d in o.Dispositions)
            {
                if (keys.TryGetValue(d.ImpactKey, out EcImpactItemDto it)) { d.Text = it.Text; d.PartNumber = it.PartNumber; d.Quantity = it.Quantity; d.Category = it.Category; d.Stale = false; }
                else d.Stale = true;
            }
            HashSet<string> have = o.Dispositions.Select(d => d.ImpactKey).ToHashSet();
            foreach (EcImpactItemDto it in snap.Items.Where(i => !have.Contains(i.Key)))
                o.Dispositions.Add(new EcDispositionItem { OrderId = o.Id, Category = it.Category, ImpactKey = it.Key, Text = it.Text, PartNumber = it.PartNumber, Quantity = it.Quantity });
            await RecomputePartsAsync(o, ct);
        }

        public async Task<ResponseDto<EcOrderDto>> SaveDispositionAsync(EcDispositionSaveDto dto, CancellationToken ct)
        {
            EcOrder o = dto == null ? null : await LoadOrderAsync(dto.OrderId, true, ct);
            if (o == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
            string e = CheckEditable(o);
            if (e != null) return Bad<EcOrderDto>(e);
            EcDispositionItem d = o.Dispositions.FirstOrDefault(x => x.ImpactKey == dto.ImpactKey && !x.Stale);
            if (d == null) return Bad<EcOrderDto>("این مورد در تحلیل اثر نیست.");
            string problem = await ApplyDispositionAsync(d, dto.Disposition, dto.Note, dto.OwnerUserId, dto.DueDate, ct);
            if (problem != null) return Bad<EcOrderDto>(problem);
            o.ContentSha256 = null;
            Touch(o);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcOrderDto>(conflict);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(await LoadOrderAsync(o.Id, false, ct), ct));
        }

        private async Task<string> ApplyDispositionAsync(EcDispositionItem d, EcDisposition? disposition, string note, int? owner, DateTime? due, CancellationToken ct)
        {
            if (disposition.HasValue && !EcRules.IsAllowed(d.Category, disposition.Value)) return $"تکلیف «{EcRules.Title(disposition.Value)}» برای «{EcRules.Title(d.Category)}» مجاز نیست.";
            if (owner.HasValue && !await _uow.Set<Entities.Identity.User>().AnyAsync(u => u.Id == owner.Value, ct)) return "مسئول اجرا پیدا نشد.";
            d.Disposition = disposition; d.Note = Clean(note); d.OwnerUserId = owner; d.DueDate = due?.Date;
            d.DecidedByUserId = disposition.HasValue ? Me : (int?)null; d.DecidedAt = disposition.HasValue ? DateTimeOffset.Now : (DateTimeOffset?)null;
            return null;
        }

        public async Task<ResponseDto<EcOrderDto>> BulkDispositionAsync(EcBulkDispositionDto dto, CancellationToken ct)
        {
            EcOrder o = dto == null ? null : await LoadOrderAsync(dto.OrderId, true, ct);
            if (o == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
            string e = CheckEditable(o);
            if (e != null) return Bad<EcOrderDto>(e);
            var rows = o.Dispositions.Where(d => !d.Stale && d.Category == dto.Category).ToList();
            if (rows.Count == 0) return Bad<EcOrderDto>("در این فهرست مورد اثر‌پذیری نیست.");
            foreach (EcDispositionItem d in rows)
            {
                string problem = await ApplyDispositionAsync(d, dto.Disposition, dto.Note, dto.OwnerUserId, dto.DueDate, ct);
                if (problem != null) return Bad<EcOrderDto>(problem);
            }
            o.ContentSha256 = null;
            Touch(o);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcOrderDto>(conflict);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(await LoadOrderAsync(o.Id, false, ct), ct));
        }

        // ============================================================================================== workflow

        public async Task<ResponseDto<EcOrderDto>> OrderActionAsync(EcOrderActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<EcOrderDto>(AiNeverApproves);
            EcOrder o = dto == null ? null : await LoadOrderAsync(dto.Id, true, ct);
            if (o == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            string tellKey = $"ec:eco:{o.Id}";
            switch (action)
            {
                case "refresh-impact":
                {
                    string e = CheckEditable(o);
                    if (e != null) return Bad<EcOrderDto>(e);
                    await RefreshImpactCoreAsync(o, ct);
                    o.ContentSha256 = null;
                    Touch(o);
                    break;
                }
                case "submit":
                {
                    string e = CheckEditable(o);
                    if (e != null) return Bad<EcOrderDto>(e);
                    List<string> blockers = await BlockersAsync(o, true, ct);
                    if (blockers.Count > 0) return Bad<EcOrderDto>("دستور تغییر هنوز آماده ارسال نیست: " + string.Join(" ", blockers));
                    o.Status = EcoStatus.InApproval; o.SubmittedAt = now; o.ContentSha256 = await HashAsync(o, ct);
                    Touch(o);
                    break;
                }
                case "approve-engineering":
                case "approve-qa":
                {
                    if (o.Status != EcoStatus.InApproval) return Bad<EcOrderDto>("دستور در حال تایید نیست.");
                    bool eng = action == "approve-engineering";
                    if (!InRole(eng ? EcRules.RoleEngineering : EcRules.RoleQa)) return Bad<EcOrderDto>(NotAllowed);
                    if ((eng ? o.EngineeringApprovedByUserId : o.QaApprovedByUserId).HasValue) return Bad<EcOrderDto>("این تایید قبلاً داده شده است.");
                    string who = ApproveProblem(o, Me, eng ? o.QaApprovedByUserId : o.EngineeringApprovedByUserId);
                    if (who != null) return Bad<EcOrderDto>(who);
                    if (await HashAsync(o, ct) != o.ContentSha256) return Bad<EcOrderDto>("محتوای دستور پس از ارسال تغییر کرده است؛ آن را بازگردانید و دوباره بفرستید.");
                    List<string> blockers = await BlockersAsync(o, true, ct);
                    if (blockers.Count > 0) return Bad<EcOrderDto>("دستور تغییر را نمی‌توان تایید کرد: " + string.Join(" ", blockers));
                    if (eng) { o.EngineeringApprovedByUserId = Me; o.EngineeringApprovedAt = now; o.EngineeringNote = note; }
                    else { o.QaApprovedByUserId = Me; o.QaApprovedAt = now; o.QaNote = note; }
                    if (o.EngineeringApprovedByUserId.HasValue && o.QaApprovedByUserId.HasValue)
                    {
                        o.Status = EcoStatus.Approved; o.ApprovedAt = now;
                        await RecomputePartsAsync(o, ct);
                        foreach (EcDispositionItem d in o.Dispositions.Where(d => !d.Stale && d.Disposition.HasValue && EcRules.NeedsTask(d.Disposition.Value)))
                        {
                            var t = new EcTask { Order = o, DispositionItemId = d.Id, Title = d.Text.Length > 480 ? d.Text.Substring(0, 480) : d.Text, Disposition = d.Disposition.Value, OwnerUserId = d.OwnerUserId.Value, DueDate = d.DueDate.Value, Status = EcTaskStatus.Open };
                            Stamp(t, true, Me);
                            o.Tasks.Add(t);
                        }
                    }
                    Stamp(o, false, Me);
                    break;
                }
                case "return":
                {
                    if (o.Status != EcoStatus.InApproval) return Bad<EcOrderDto>("دستور در حال تایید نیست.");
                    if (!InRole(EcRules.RoleEngineering) && !InRole(EcRules.RoleQa)) return Bad<EcOrderDto>(NotAllowed);
                    if (note == null) return Bad<EcOrderDto>("دلیل بازگرداندن را بنویسید.");
                    o.Status = EcoStatus.Draft; o.ContentSha256 = null; o.SubmittedAt = null;
                    o.EngineeringApprovedByUserId = null; o.EngineeringApprovedAt = null; o.EngineeringNote = null; o.QaApprovedByUserId = null; o.QaApprovedAt = null; o.QaNote = null;
                    Stamp(o, false, Me);
                    break;
                }
                case "reject":
                {
                    if (o.Status != EcoStatus.InApproval) return Bad<EcOrderDto>("فقط دستور در حال تایید رد می‌شود.");
                    if (!InRole(EcRules.RoleEngineering) && !InRole(EcRules.RoleQa)) return Bad<EcOrderDto>(NotAllowed);
                    string who = ApproveProblem(o, Me, null);
                    if (who != null) return Bad<EcOrderDto>(who);
                    if (note == null) return Bad<EcOrderDto>("دلیل رد را بنویسید.");
                    o.Status = EcoStatus.Rejected; o.RejectedByUserId = Me; o.RejectedAt = now; o.RejectReason = note;
                    Stamp(o, false, Me);
                    break;
                }
                case "cancel":
                {
                    if (o.Status != EcoStatus.Draft && o.Status != EcoStatus.InApproval) return Bad<EcOrderDto>("فقط دستور پیش‌نویس یا در حال تایید لغو می‌شود.");
                    if (o.CreatorUserId != Me && !InRole(EcRules.RoleEngineering)) return Bad<EcOrderDto>(NotAllowed);
                    if (note == null) return Bad<EcOrderDto>("دلیل لغو را بنویسید.");
                    o.Status = EcoStatus.Cancelled; o.CancelReason = note;
                    Stamp(o, false, Me);
                    break;
                }
                case "void":
                {
                    // a wrong approval has a way out: ENGINEERING or QA voids an approved order (with a reason) before it is closed; the named draft revisions are free again and the order no longer gates anything
                    if (o.Status != EcoStatus.Approved) return Bad<EcOrderDto>("فقط دستور تاییدشده باطل می‌شود.");
                    if (!InRole(EcRules.RoleEngineering) && !InRole(EcRules.RoleQa)) return Bad<EcOrderDto>(NotAllowed);
                    if (note == null) return Bad<EcOrderDto>("دلیل ابطال را بنویسید.");
                    o.Status = EcoStatus.Cancelled; o.CancelReason = "ابطال پس از تایید: " + note;
                    Stamp(o, false, Me);
                    break;
                }
                case "close":
                {
                    if (o.Status != EcoStatus.Approved) return Bad<EcOrderDto>("فقط دستور تاییدشده بسته می‌شود.");
                    if (!InRole(EcRules.RoleEngineering) && !InRole(EcRules.RoleQa)) return Bad<EcOrderDto>(NotAllowed);
                    int open = o.Tasks.Count(t => !t.Deleted && t.Status != EcTaskStatus.Verified);
                    if (open > 0) return Bad<EcOrderDto>($"{open} تکلیف اجرایی هنوز انجام یا توسط QA تایید نشده است.");
                    foreach (EcOrderChange c in o.Changes)
                        if (!await ChangeDoneAsync(c, ct)) return Bad<EcOrderDto>($"«{c.Reference}» هنوز به وضعیت نهایی (منتشر / تایید / منسوخ) نرسیده است.");
                    o.Status = EcoStatus.Closed; o.ClosedAt = now; o.ClosedByUserId = Me;
                    Stamp(o, false, Me);
                    break;
                }
                default:
                    return Bad<EcOrderDto>("عمل نامعتبر است.");
            }
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcOrderDto>(conflict);
            await AfterActionAsync(o, action, tellKey, ct);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(await LoadOrderAsync(o.Id, false, ct), ct));
        }

        private async Task AfterActionAsync(EcOrder o, string action, string tellKey, CancellationToken ct)
        {
            string link = "/eng-change/orders/" + o.Id;
            if (action == "submit" || (action == "approve-engineering" && o.Status == EcoStatus.InApproval) || (action == "approve-qa" && o.Status == EcoStatus.InApproval))
            {
                // the step that is still missing is in the inbox of its role
                bool needEng = o.EngineeringApprovedByUserId == null, needQa = o.QaApprovedByUserId == null;
                string roles = (needEng ? EcRules.RoleEngineering : "") + (needEng && needQa ? "," : "") + (needQa ? EcRules.RoleQa : "");
                await Tell(tellKey, $"دستور تغییر مهندسی {o.Number} منتظر تایید است", "EcOrder", o.Id, link, roles, null, true, ct);
                return;
            }
            if (action == "approve-engineering" || action == "approve-qa")
            {
                await CloseTell(tellKey, "دستور تایید شد", ct);
                var users = new HashSet<int>(await ProjectManagersAsync(o, ct));
                foreach (EcTask t in o.Tasks) users.Add(t.OwnerUserId);
                await Tell($"ec:eco:info:{o.Id}", $"دستور تغییر مهندسی {o.Number} تایید و ابلاغ شد ({EcRules.EffectivityText(o)})", "EcOrder", o.Id, link, EcRules.Informed, users, false, ct);
                return;
            }
            if (action == "return" || action == "reject" || action == "cancel" || action == "void" || action == "close") await CloseTell(tellKey, "پایان", ct);
            if (action == "reject" || action == "cancel" || action == "void") await CloseTell($"ec:eco:info:{o.Id}", "پایان", ct);
        }

        /// <summary>The project managers of the projects the order touches (the project baselines and the production orders of the impact).</summary>
        private async Task<List<int>> ProjectManagersAsync(EcOrder o, CancellationToken ct)
        {
            EcImpactBuilder.Snapshot snap = EcImpactBuilder.Parse(o.ImpactJson);
            if (snap == null) return new List<int>();
            List<int> baselineIds = snap.Items.Where(i => i.Category == EcImpactCategory.Baseline && i.RefId.HasValue).Select(i => i.RefId.Value).ToList();
            List<int> orderIds = snap.Items.Where(i => i.Category == EcImpactCategory.Order && i.Key.StartsWith("ORDER:", StringComparison.Ordinal) && i.RefId.HasValue).Select(i => i.RefId.Value).ToList();
            var workOrderIds = new HashSet<int>();
            if (baselineIds.Count > 0) foreach (int w in await _uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => baselineIds.Contains(b.Id)).Select(b => b.WorkOrderId).ToListAsync(ct)) workOrderIds.Add(w);
            if (orderIds.Count > 0) foreach (int w in await _uow.Set<MfProductionOrder>().AsNoTracking().Where(b => orderIds.Contains(b.Id)).Select(b => b.WorkOrderId).ToListAsync(ct)) workOrderIds.Add(w);
            if (workOrderIds.Count == 0) return new List<int>();
            List<int> contracts = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => workOrderIds.Contains(w.Id) && w.ContractId > 0).Select(w => w.ContractId).Distinct().ToListAsync(ct);
            if (contracts.Count == 0) return new List<int>();
            return await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => contracts.Contains(t.ContractId) && t.ProjectManagerUserId != null).Select(t => t.ProjectManagerUserId.Value).Distinct().ToListAsync(ct);
        }

        // ============================================================================================== tasks

        public async Task<ResponseDto<EcOrderDto>> TaskActionAsync(EcTaskActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<EcOrderDto>(AiNeverApproves);
            EcTask t = dto == null ? null : await _uow.Set<EcTask>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (t == null) return ResponseDtoHelper.NotFound<EcOrderDto>(NotFound);
            EcOrder o = await _uow.Set<EcOrder>().AsNoTracking().FirstAsync(x => x.Id == t.OrderId, ct);
            if (o.Status != EcoStatus.Approved) return Bad<EcOrderDto>("تکلیف‌ها فقط برای دستور تاییدشده انجام می‌شوند.");
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            string note = Clean(dto.Note);
            if (action == "done")
            {
                if (t.Status != EcTaskStatus.Open) return Bad<EcOrderDto>("این تکلیف قبلاً انجام شده است.");
                if (t.OwnerUserId != Me && !InRole(EcRules.RoleEngineering)) return Bad<EcOrderDto>("تکلیف را مسئول آن یا مدیر مهندسی انجام‌شده ثبت می‌کند.");
                if (note == null) return Bad<EcOrderDto>("شرح انجام کار را بنویسید.");
                t.Status = EcTaskStatus.Done; t.DoneAt = DateTimeOffset.Now; t.DoneByUserId = Me; t.DoneNote = note;
            }
            else if (action == "verify")
            {
                if (t.Status != EcTaskStatus.Done) return Bad<EcOrderDto>("فقط تکلیف انجام‌شده تایید می‌شود.");
                if (!InRole(EcRules.RoleQa)) return Bad<EcOrderDto>("تایید اجرای تکلیف با تضمین کیفیت (QA) است.");
                if (t.DoneByUserId == Me) return Bad<EcOrderDto>("کسی که تکلیف را انجام داده آن را تایید نمی‌کند.");
                t.Status = EcTaskStatus.Verified; t.VerifiedAt = DateTimeOffset.Now; t.VerifiedByUserId = Me; t.VerifyNote = note;
            }
            else return Bad<EcOrderDto>("عمل نامعتبر است.");
            Stamp(t, false, Me);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcOrderDto>(conflict);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(await LoadOrderAsync(o.Id, false, ct), ct));
        }

        public async Task<ResponseDto<List<EcTaskDto>>> GetMyTasksAsync(CancellationToken ct)
        {
            if (!InRole(EcRules.Readers)) return Bad<List<EcTaskDto>>(NotAllowed);
            List<EcTask> rows = await _uow.Set<EcTask>().AsNoTracking().Where(t => !t.Deleted && t.OwnerUserId == Me && t.Status != EcTaskStatus.Verified && t.Order.Status == EcoStatus.Approved).OrderBy(t => t.DueDate).Take(300).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.Select(t => new EcTaskDto
            {
                Id = t.Id, DispositionItemId = t.DispositionItemId, Title = t.Title, Disposition = t.Disposition, DispositionTitle = EcRules.Title(t.Disposition), OwnerUserId = t.OwnerUserId, DueDate = t.DueDate, Status = t.Status,
                StatusText = t.Status == EcTaskStatus.Open ? "باز" : "انجام شده، منتظر تایید QA", Overdue = t.Status == EcTaskStatus.Open && t.DueDate.Date < DateTime.Today, CanDone = t.Status == EcTaskStatus.Open && !IsAi,
            }).ToList());
        }

        // ============================================================================================== history and gate check

        public async Task<ResponseDto<List<EcHistoryRowDto>>> GetHistoryAsync(string partNumber, CancellationToken ct)
        {
            if (!InRole(EcRules.Readers)) return Bad<List<EcHistoryRowDto>>(NotAllowed);
            string pn = Clean(partNumber)?.ToUpperInvariant();
            if (pn == null) return Bad<List<EcHistoryRowDto>>("شماره قطعه یا محصول را بنویسید.");
            string needle = "," + pn + ",";
            var rows = new List<EcHistoryRowDto>();
            foreach (EcRequest r in await _uow.Set<EcRequest>().AsNoTracking().Include(x => x.Targets).Where(r => !r.Deleted && r.Targets.Any(t => t.PartNumber == pn)).OrderBy(r => r.Id).ToListAsync(ct))
                rows.Add(new EcHistoryRowDto { Kind = "ECR", Id = r.Id, Number = r.Number, Title = r.Title, StatusText = StatusText(r.Status), Detail = r.Reason, At = r.CreateDate });
            foreach (EcOrder o in await _uow.Set<EcOrder>().AsNoTracking().Include(x => x.Changes).Where(o => !o.Deleted && o.AffectedParts != null && o.AffectedParts.Contains(needle)).OrderBy(o => o.Id).ToListAsync(ct))
                rows.Add(new EcHistoryRowDto { Kind = "ECO", Id = o.Id, Number = o.Number, Title = o.Title, StatusText = StatusText(o.Status), Detail = string.Join("؛ ", o.Changes.Select(c => c.Reference + (c.Action == EcChangeAction.Obsolete ? " (منسوخ)" : ""))), Effectivity = EcRules.EffectivityText(o), At = o.ApprovedAt ?? o.CreateDate });
            return ResponseDtoHelper.Ok(rows.OrderBy(r => r.At).ToList());
        }

        public async Task<ResponseDto<EcGateCheckDto>> CheckAsync(EcChangeKind kind, int targetId, EcChangeAction action, CancellationToken ct)
        {
            if (!InRole(EcRules.Readers)) return Bad<EcGateCheckDto>(NotAllowed);
            var c = new EcOrderChange { Kind = kind, Action = action, TargetId = targetId };
            string problem = await FillChangeAsync(c, ct);
            EcOrder eco = await EcGate.ApprovedOrderForAsync(_uow, kind, targetId, action, ct);
            var dto = new EcGateCheckDto { Kind = kind, TargetId = targetId, OrderNumber = eco?.Number };
            if (eco != null) { dto.Required = true; dto.Ok = true; dto.Message = "دستور تغییر تاییدشده " + eco.Number + " این بازنگری را نام برده است."; return ResponseDtoHelper.Ok(dto); }
            if (c.OldTargetId == null && action == EcChangeAction.NewRevision) { dto.Required = false; dto.Ok = true; dto.Message = problem ?? "برای بازنگری اول دستور تغییر لازم نیست."; return ResponseDtoHelper.Ok(dto); }
            dto.Required = true; dto.Ok = false;
            dto.Message = "دستور تغییر تاییدشده‌ای که این بازنگری را نام ببرد وجود ندارد.";
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
