using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.ProductBank
{
    public interface IPbBomService
    {
        Task<ResponseDto<PagedList<PbBomListDto>>> GetListAsync(PbBomQuery query, CancellationToken ct);
        Task<ResponseDto<PbBomDto>> GetAsync(int id, CancellationToken ct);
        Task<ResponseDto<PbBomDto>> SaveAsync(SavePbBomDto dto, CancellationToken ct);
        Task<ResponseDto<PbBomDto>> SubmitAsync(PbBomActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbBomDto>> ReturnAsync(PbBomActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbBomDto>> SignAsync(PbSignDto dto, CancellationToken ct);
        Task<ResponseDto<PbBomDto>> ReleaseAsync(PbBomActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbBomDto>> NewRevisionAsync(PbBomActionDto dto, CancellationToken ct);
        Task<ResponseDto<PbBomDto>> ObsoleteAsync(PbBomActionDto dto, CancellationToken ct);
        Task<ResponseDto<bool>> DeleteDraftAsync(int id, CancellationToken ct);
        Task<ResponseDto<PbBomDiffDto>> DiffAsync(int fromId, int toId, CancellationToken ct);
        Task<ResponseDto<PbWhereUsedDto>> WhereUsedAsync(string partNumber, CancellationToken ct);
    }

    /// <summary>
    /// PB1 part 2: the BOM master. A BOM revision is signed in four steps after the preparer (checked, QC checked, manufacturing checked, approved), every signature by a different person, then released.
    /// Only a Draft revision changes; a released revision is never edited: a new revision copies it and the diff shows what changed. The legacy Bom / EquipmentBom stay as they were.
    /// </summary>
    public class PbBomService : PbServiceBase, IPbBomService
    {
        public static readonly PbBomSignature[] Order = { PbBomSignature.Checked, PbBomSignature.QcChecked, PbBomSignature.ManufacturingChecked, PbBomSignature.Approved };

        public static string StepRoles(PbBomSignature step)
        {
            switch (step)
            {
                case PbBomSignature.Checked: return PbRoles.CheckRoles;
                case PbBomSignature.QcChecked: return PbRoles.QcRoles;
                case PbBomSignature.ManufacturingChecked: return PbRoles.ManufacturingRoles;
                case PbBomSignature.Approved: return PbRoles.ApproveRoles;
                default: return PbRoles.Writers;
            }
        }

        public static string StepTitle(PbBomSignature step)
        {
            switch (step)
            {
                case PbBomSignature.Prepared: return "تهیه‌کننده";
                case PbBomSignature.Checked: return "کنترل‌کننده";
                case PbBomSignature.QcChecked: return "کنترل کیفیت";
                case PbBomSignature.ManufacturingChecked: return "کنترل ساخت";
                default: return "تاییدکننده";
            }
        }

        public PbBomService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user)
        {
        }

        // -------------------------------------------------------------------------------- reading

        public async Task<ResponseDto<PagedList<PbBomListDto>>> GetListAsync(PbBomQuery query, CancellationToken ct)
        {
            query ??= new PbBomQuery();
            IQueryable<PbBomHeader> q = Uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted);
            if (query.ProductId.HasValue) q = q.Where(h => h.ProductId == query.ProductId.Value);
            if (query.Status.HasValue) q = q.Where(h => h.Status == query.Status.Value);
            string text = PbText.Clean(query.Contains);
            if (text != null) q = q.Where(h => h.Code.Contains(text) || h.Title.Contains(text) || h.AssemblyPartNumber.Contains(text));
            IQueryable<PbBomListDto> projected = q.OrderBy(h => h.Code).ThenByDescending(h => h.Revision).Select(h => new PbBomListDto
            {
                Id = h.Id, ProductId = h.ProductId, Code = h.Code, Revision = h.Revision, Title = h.Title, AssemblyPartNumber = h.AssemblyPartNumber, Status = h.Status,
                DocumentDate = h.DocumentDate, EffectiveFrom = h.EffectiveFrom, PreparedByUserId = h.PreparedByUserId, CreateDate = h.CreateDate,
                LineCount = h.Lines.Count(l => !l.Deleted && !l.IsHeading),
            });
            PagedList<PbBomListDto> page = await PagedList<PbBomListDto>.CreateAsync(projected, query.PageNumber, query.PageSize, ct);
            foreach (PbBomListDto d in page) d.RevisionText = PbText.Revision(d.Revision);
            return ResponseDtoHelper.Ok(page);
        }

        private async Task<PbBomHeader> LoadAsync(int id, bool tracked, CancellationToken ct)
        {
            IQueryable<PbBomHeader> q = Uow.Set<PbBomHeader>().Where(h => h.Id == id && !h.Deleted);
            q = tracked ? q.Include(h => h.Lines) : q.AsNoTracking().Include(h => h.Lines);
            return await q.FirstOrDefaultAsync(ct);
        }

        private static List<PbBomLine> LiveLines(PbBomHeader h) => h.Lines.Where(l => !l.Deleted).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToList();

        private static decimal? EffectiveScrap(PbBomLine l)
        {
            if (l.ScrapFactor.HasValue) return l.ScrapFactor.Value;
            if (l.NetWeight.HasValue && l.NetWeight.Value > 0 && l.GrossWeight.HasValue) return Math.Round(l.GrossWeight.Value / l.NetWeight.Value - 1m, 4);
            return null;
        }

        private async Task<bool> HasOpenRevisionAsync(string code, int exceptId, CancellationToken ct) =>
            await Uow.Set<PbBomHeader>().AnyAsync(h => !h.Deleted && h.Code == code && h.Id != exceptId &&
                (h.Status == PbBomStatus.Draft || h.Status == PbBomStatus.UnderReview || h.Status == PbBomStatus.Approved), ct);

        private async Task<PbBomDto> ToDtoAsync(PbBomHeader h, CancellationToken ct)
        {
            List<PbBomLine> lines = LiveLines(h);
            List<int> itemIds = lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value).Distinct().ToList();
            Dictionary<int, string> itemCodes = itemIds.Count == 0 ? new Dictionary<int, string>() : await Uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct);
            bool distinct = await DistinctReviewersAsync(ct);
            var dto = new PbBomDto
            {
                Id = h.Id, ProductId = h.ProductId, Code = h.Code, Revision = h.Revision, RevisionText = PbText.Revision(h.Revision), Title = h.Title, AssemblyPartNumber = h.AssemblyPartNumber,
                Status = h.Status, DocumentDate = h.DocumentDate, EffectiveFrom = h.EffectiveFrom, PreparedByUserId = h.PreparedByUserId, CreateDate = h.CreateDate,
                LineCount = lines.Count(l => !l.IsHeading), Platform = h.Platform, IssueFor = h.IssueFor, ChangeReason = h.ChangeReason, SupersedesId = h.SupersedesId, ContentHash = h.ContentHash,
                ImportedSigned = h.ImportedSigned, PaperSignatureNote = h.PaperSignatureNote, PreparedAt = h.PreparedAt, ReleasedByUserId = h.ReleasedByUserId, ReleasedAt = h.ReleasedAt,
            };
            foreach (PbBomLine l in lines)
            {
                dto.Lines.Add(new PbBomLineDto
                {
                    Id = l.Id, SortOrder = l.SortOrder, ItemNumber = l.ItemNumber, IsHeading = l.IsHeading, ParentLineId = l.ParentLineId, AlternativeOfLineId = l.AlternativeOfLineId, PartNumber = l.PartNumber, ItemId = l.ItemId,
                    ItemCode = l.ItemId.HasValue && itemCodes.TryGetValue(l.ItemId.Value, out string c) ? c : null, PartName = l.PartName, Qty = l.Qty, Unit = l.Unit, Standard = l.Standard,
                    Material = l.Material, RawDiameter = l.RawDiameter, RawLength = l.RawLength, RawWidth = l.RawWidth, RawThickness = l.RawThickness, NetWeight = l.NetWeight,
                    GrossWeight = l.GrossWeight, ScrapFactor = l.ScrapFactor, ScrapFactorEffective = EffectiveScrap(l), MakeOrBuy = l.MakeOrBuy, Description = l.Description, ChildBomId = l.ChildBomId,
                });
            }
            dto.Signatures.Add(new PbSignatureDto { Step = PbBomSignature.Prepared, Title = StepTitle(PbBomSignature.Prepared), Roles = PbRoles.Writers, UserId = h.PreparedByUserId, At = h.PreparedAt, Done = h.PreparedAt.HasValue || h.ImportedSigned });
            dto.Signatures.Add(new PbSignatureDto { Step = PbBomSignature.Checked, Title = StepTitle(PbBomSignature.Checked), Roles = PbRoles.CheckRoles, UserId = h.CheckedByUserId, At = h.CheckedAt, Done = h.CheckedAt.HasValue || h.ImportedSigned });
            dto.Signatures.Add(new PbSignatureDto { Step = PbBomSignature.QcChecked, Title = StepTitle(PbBomSignature.QcChecked), Roles = PbRoles.QcRoles, UserId = h.QcCheckedByUserId, At = h.QcCheckedAt, Done = h.QcCheckedAt.HasValue || h.ImportedSigned });
            dto.Signatures.Add(new PbSignatureDto { Step = PbBomSignature.ManufacturingChecked, Title = StepTitle(PbBomSignature.ManufacturingChecked), Roles = PbRoles.ManufacturingRoles, UserId = h.ManufacturingCheckedByUserId, At = h.ManufacturingCheckedAt, Done = h.ManufacturingCheckedAt.HasValue || h.ImportedSigned });
            dto.Signatures.Add(new PbSignatureDto { Step = PbBomSignature.Approved, Title = StepTitle(PbBomSignature.Approved), Roles = PbRoles.ApproveRoles, UserId = h.ApprovedByUserId, At = h.ApprovedAt, Done = h.ApprovedAt.HasValue || h.ImportedSigned });

            // the names of the signers (one query), so the screen shows people and not user numbers
            var ids = dto.Signatures.Where(x => x.UserId.HasValue).Select(x => x.UserId.Value).Distinct().ToList();
            if (ids.Count > 0)
            {
                var people = await Uow.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.LastName, u.UserName }).ToListAsync(ct);
                foreach (PbSignatureDto sg in dto.Signatures.Where(x => x.UserId.HasValue))
                {
                    var u = people.FirstOrDefault(x => x.Id == sg.UserId.Value);
                    if (u == null) continue;
                    string full = ((u.Name ?? "") + " " + (u.LastName ?? "")).Trim();
                    sg.UserName = full.Length == 0 ? u.UserName : full;
                }
            }

            PbBomSignature? next = NextStep(h);
            dto.NextSignature = next.HasValue ? (int?)next.Value : null;
            bool human = !IsAi;
            dto.CanEdit = human && h.Status == PbBomStatus.Draft && h.PreparedByUserId == User.UserId && InRole(PbRoles.Writers);
            dto.CanSubmit = dto.CanEdit;
            dto.CanSign = human && next.HasValue && h.Status == PbBomStatus.UnderReview && InRole(StepRoles(next.Value)) && SignerError(h, User.UserId, distinct) == null;
            dto.DistinctReviewers = distinct;
            dto.CanRelease = human && h.Status == PbBomStatus.Approved && InRole(PbRoles.ReleaseRoles) && h.PreparedByUserId != User.UserId;
            dto.CanNewRevision = human && h.Status == PbBomStatus.Released && InRole(PbRoles.Writers) && !await HasOpenRevisionAsync(h.Code, h.Id, ct);
            dto.CanObsolete = human && (h.Status == PbBomStatus.Released || h.Status == PbBomStatus.Superseded) && InRole(PbRoles.ReleaseRoles);
            return dto;
        }

        private static PbBomSignature? NextStep(PbBomHeader h)
        {
            if (h.Status != PbBomStatus.UnderReview) return null;
            if (!h.CheckedAt.HasValue) return PbBomSignature.Checked;
            if (!h.QcCheckedAt.HasValue) return PbBomSignature.QcChecked;
            if (!h.ManufacturingCheckedAt.HasValue) return PbBomSignature.ManufacturingChecked;
            if (!h.ApprovedAt.HasValue) return PbBomSignature.Approved;
            return null;
        }

        /// <summary>
        /// Maker-checker: the preparer never signs any other step. Whether the four reviewers (checked, QC checked, manufacturing checked, approved) must also differ from each other is a setting
        /// (BomSignatures:DistinctReviewers, default true since CEO decision D13: the four signatures are four distinct persons).
        /// </summary>
        private static string SignerError(PbBomHeader h, int userId, bool distinctReviewers)
        {
            if (userId <= 0) return PbMessages.NotAllowed;
            if (h.PreparedByUserId == userId) return PbMessages.SameUser;
            if (distinctReviewers && (h.CheckedByUserId == userId || h.QcCheckedByUserId == userId || h.ManufacturingCheckedByUserId == userId || h.ApprovedByUserId == userId))
                return PbMessages.SameUserReviewers;
            return null;
        }

        public async Task<ResponseDto<PbBomDto>> GetAsync(int id, CancellationToken ct)
        {
            PbBomHeader h = await LoadAsync(id, false, ct);
            return h == null ? ResponseDtoHelper.NotFound<PbBomDto>(PbMessages.NotFound) : ResponseDtoHelper.Ok(await ToDtoAsync(h, ct));
        }

        // -------------------------------------------------------------------------------- the content hash

        /// <summary>SHA-256 over every field of every line (the parent is named by its sort order), so any change of a submitted or released revision shows.</summary>
        public static string HashOf(IEnumerable<PbBomLine> lines)
        {
            List<PbBomLine> list = lines.Where(l => !l.Deleted).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToList();
            var order = list.Select((l, i) => new { l.Id, i }).ToDictionary(x => x.Id, x => x.i);
            var sb = new StringBuilder();
            foreach (PbBomLine l in list)
            {
                int parent = l.ParentLineId.HasValue && order.TryGetValue(l.ParentLineId.Value, out int p) ? p : -1;
                int alt = l.AlternativeOfLineId.HasValue && order.TryGetValue(l.AlternativeOfLineId.Value, out int a) ? a : -1;
                sb.Append(string.Join("|", l.ItemNumber?.ToString(CultureInfo.InvariantCulture), l.IsHeading ? "H" : "L", parent.ToString(CultureInfo.InvariantCulture) + "/" + alt.ToString(CultureInfo.InvariantCulture), l.PartNumber, l.PartName,
                    PbText.Num(l.Qty), l.Unit, l.Standard, l.Material, PbText.Num(l.RawDiameter), PbText.Num(l.RawLength), PbText.Num(l.RawWidth), PbText.Num(l.RawThickness), PbText.Num(l.NetWeight),
                    PbText.Num(l.GrossWeight), PbText.Num(l.ScrapFactor), ((int)l.MakeOrBuy).ToString(CultureInfo.InvariantCulture), l.Description, l.ChildBomId?.ToString(CultureInfo.InvariantCulture))).Append('\n');
            }
            return PbText.Sha256(sb.ToString());
        }

        // -------------------------------------------------------------------------------- saving a draft

        public async Task<ResponseDto<PbBomDto>> SaveAsync(SavePbBomDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.AiNeverApproves);
            if (!InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.NotAllowed);
            if (dto == null) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.InvalidData);
            string assembly = PbText.PartNumber(dto.AssemblyPartNumber);
            if (assembly == null) return ResponseDtoHelper.BadRequest<PbBomDto>("شماره قطعه مجموعه (والد) را وارد کنید.");
            string shape = PbCodeRules.ShapeError(assembly);
            if (shape != null) return ResponseDtoHelper.BadRequest<PbBomDto>("شماره مجموعه: " + shape);
            string error = ValidateLines(assembly, dto.Lines);
            if (error != null) return ResponseDtoHelper.BadRequest<PbBomDto>(error);

            PbBomHeader h;
            if (dto.Id.HasValue)
            {
                h = await LoadAsync(dto.Id.Value, true, ct);
                if (h == null) return ResponseDtoHelper.NotFound<PbBomDto>(PbMessages.NotFound);
                if (h.Status != PbBomStatus.Draft) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.OnlyDraft);
                if (h.PreparedByUserId != User.UserId) return ResponseDtoHelper.BadRequest<PbBomDto>("پیش‌نویس را فقط تهیه‌کننده آن ویرایش می‌کند.");
                if (assembly != h.AssemblyPartNumber) return ResponseDtoHelper.BadRequest<PbBomDto>("شماره مجموعه یک نسخه تغییر نمی‌کند.");
                Uow.Set<PbBomLine>().RemoveRange(h.Lines);
                h.Lines.Clear();
            }
            else
            {
                string code = assembly + "-BOM";
                if (await Uow.Set<PbBomHeader>().AnyAsync(x => !x.Deleted && x.Code == code, ct))
                    return ResponseDtoHelper.BadRequest<PbBomDto>("BOM " + code + " قبلاً ثبت شده است؛ از «نسخه جدید» همان BOM استفاده کنید.");
                h = new PbBomHeader { Code = code, Revision = 0, Status = PbBomStatus.Draft, AssemblyPartNumber = assembly, PreparedByUserId = User.UserId };
                Uow.Set<PbBomHeader>().Add(h);
                Stamp(h, true);
            }
            int? productId = dto.ProductId;
            if (!productId.HasValue)
                productId = await Uow.Set<PbProduct>().Where(p => !p.Deleted && p.Code == assembly).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
            else if (!await Uow.Set<PbProduct>().AnyAsync(p => p.Id == productId.Value && !p.Deleted, ct))
                return ResponseDtoHelper.BadRequest<PbBomDto>("محصول پیدا نشد.");
            h.ProductId = productId;
            h.Title = PbText.Clean(dto.Title);
            h.Platform = PbText.Clean(dto.Platform);
            h.IssueFor = PbText.Clean(dto.IssueFor);
            h.DocumentDate = dto.DocumentDate;
            h.ChangeReason = PbText.Clean(dto.ChangeReason) ?? h.ChangeReason;
            Stamp(h, false);

            string childError = await AddLinesAsync(h, dto.Lines, ct);
            if (childError != null) return ResponseDtoHelper.BadRequest<PbBomDto>(childError);
            await Uow.SaveChangesAsync(ct);
            // the parents and alternatives are set by their sort order in a second pass (the keys exist only after the first save)
            await LinkParentsAsync(h, dto.Lines, ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(await LoadAsync(h.Id, false, ct), ct));
        }

        private static string ValidateLines(string assembly, List<SavePbBomLineDto> lines)
        {
            if (lines == null) return PbMessages.InvalidData;
            if (lines.Count > 2000) return "حداکثر ۲۰۰۰ سطر در یک BOM.";
            for (int i = 0; i < lines.Count; i++)
            {
                SavePbBomLineDto l = lines[i];
                string where = $"سطر {i + 1}: ";
                if (l.ParentIndex.HasValue && (l.ParentIndex.Value < 0 || l.ParentIndex.Value >= lines.Count || l.ParentIndex.Value >= i || !lines[l.ParentIndex.Value].IsHeading))
                    return where + "والد باید یک سطر عنوان پیش از همین سطر باشد.";
                if (l.IsHeading)
                {
                    if (PbText.Clean(l.PartName) == null) return where + "عنوان گروه را بنویسید.";
                    if (l.AlternativeOfIndex.HasValue) return where + "سطر عنوان جایگزین سطر دیگر نیست.";
                    continue;
                }
                if (l.AlternativeOfIndex.HasValue)
                {
                    int a = l.AlternativeOfIndex.Value;
                    if (a < 0 || a >= i || lines[a].IsHeading || lines[a].AlternativeOfIndex.HasValue) return where + "جایگزین باید یک قطعهٔ اصلی پیش از همین سطر باشد.";
                }
                string pn = PbText.PartNumber(l.PartNumber);
                if (pn == null) return where + PbMessages.PartNumberRequired;
                string shape = PbCodeRules.ShapeError(pn);
                if (shape != null) return where + shape;
                if (pn == assembly) return where + "یک مجموعه خودش را شامل نمی‌شود (شمارهٔ قطعه همان شمارهٔ مجموعه است).";
                if (PbText.Clean(l.PartName) == null) return where + "نام قطعه را بنویسید.";
                if (l.Qty <= 0) return where + "تعداد باید بیشتر از صفر باشد.";
                if (!Enum.IsDefined(typeof(PbMakeOrBuy), l.MakeOrBuy)) return where + "وضعیت ساخت یا خرید نامعتبر است.";
                if (l.NetWeight < 0 || l.GrossWeight < 0 || l.RawDiameter < 0 || l.RawLength < 0 || l.RawWidth < 0 || l.RawThickness < 0) return where + "وزن و ابعاد منفی نیست.";
                if (l.NetWeight.HasValue && l.GrossWeight.HasValue && l.GrossWeight.Value < l.NetWeight.Value) return where + "وزن ناخالص از وزن خالص کمتر است.";
                if (l.ScrapFactor.HasValue && (l.ScrapFactor.Value < 0 || l.ScrapFactor.Value > 10)) return where + "ضریب ضایعات بین صفر و ده است.";
                if (l.ItemNumber.HasValue && l.ItemNumber.Value <= 0) return where + "شماره ردیف نامعتبر است.";
            }
            return null;
        }

        /// <summary>
        /// Part numbers that contain the assembly (directly or through other BOMs, with or without a child BOM link) cannot be lines of it: the assembly would include itself.
        /// One reverse walk over the (assembly, part) pairs of all other BOMs; the pairs are read once.
        /// </summary>
        private async Task<HashSet<string>> ContainersOfAsync(int excludeHeaderId, string assembly, CancellationToken ct)
        {
            var pairs = await (from l in Uow.Set<PbBomLine>().AsNoTracking()
                               join h in Uow.Set<PbBomHeader>().AsNoTracking() on l.BomHeaderId equals h.Id
                               where !l.Deleted && !h.Deleted && !l.IsHeading && h.Id != excludeHeaderId && l.PartNumber != null
                               select new { Container = h.AssemblyPartNumber, Part = l.PartNumber, Child = l.ChildBom.AssemblyPartNumber }).Distinct().ToListAsync(ct);
            var reverse = new Dictionary<string, HashSet<string>>();
            void Edge(string contained, string container)
            {
                if (contained == null || container == null) return;
                if (!reverse.TryGetValue(contained, out HashSet<string> set)) reverse[contained] = set = new HashSet<string>();
                set.Add(container);
            }
            foreach (var p in pairs) { Edge(p.Part, p.Container); Edge(p.Child, p.Container); }
            var found = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(assembly);
            while (queue.Count > 0)
            {
                string node = queue.Dequeue();
                if (!reverse.TryGetValue(node, out HashSet<string> parents)) continue;
                foreach (string c in parents) if (found.Add(c)) queue.Enqueue(c);
            }
            found.Add(assembly);
            return found;
        }

        private async Task<string> AddLinesAsync(PbBomHeader h, List<SavePbBomLineDto> lines, CancellationToken ct)
        {
            // the items and child BOMs the lines name are read once
            List<string> pns = lines.Where(l => !l.IsHeading).Select(l => PbText.PartNumber(l.PartNumber)).Where(x => x != null).Distinct().ToList();
            var itemIds = new Dictionary<string, int>();
            foreach (var i in await Uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.StageCode2 == null && pns.Contains(i.PartNumber)).Select(i => new { i.PartNumber, i.Id }).ToListAsync(ct))
                itemIds[i.PartNumber] = i.Id;
            List<int> childIds = lines.Where(l => l.ChildBomId.HasValue).Select(l => l.ChildBomId.Value).Distinct().ToList();
            var children = childIds.Count == 0 ? new Dictionary<int, PbBomHeader>() : await Uow.Set<PbBomHeader>().AsNoTracking().Where(x => childIds.Contains(x.Id) && !x.Deleted).ToDictionaryAsync(x => x.Id, ct);
            HashSet<string> containers = await ContainersOfAsync(h.Id > 0 && Uow.Entry(h).State != EntityState.Added ? h.Id : 0, h.AssemblyPartNumber, ct);
            for (int i = 0; i < lines.Count; i++)
            {
                SavePbBomLineDto l = lines[i];
                string pn = l.IsHeading ? null : PbText.PartNumber(l.PartNumber);
                if (pn != null && containers.Contains(pn)) return $"سطر {i + 1}: {pn} خودِ این مجموعه را (مستقیم یا از راه BOM دیگر) در بر دارد و نمی‌تواند زیرمجموعهٔ آن باشد (حلقه).";
                if (l.ChildBomId.HasValue)
                {
                    if (!children.TryGetValue(l.ChildBomId.Value, out PbBomHeader child)) return $"سطر {i + 1}: BOM زیرمجموعه پیدا نشد.";
                    if (child.AssemblyPartNumber != pn) return $"سطر {i + 1}: BOM زیرمجموعه باید BOM همین قطعه ({pn}) باشد.";
                    if (containers.Contains(child.AssemblyPartNumber)) return $"سطر {i + 1}: BOM زیرمجموعه به همین مجموعه برمی‌گردد (حلقه).";
                }
                var line = new PbBomLine
                {
                    SortOrder = i + 1, ItemNumber = l.ItemNumber, IsHeading = l.IsHeading, PartNumber = pn, PartName = PbText.Clean(l.PartName),
                    ItemId = pn != null && itemIds.TryGetValue(pn, out int id) ? id : (int?)null,
                    Qty = l.IsHeading ? 0 : l.Qty, Unit = l.IsHeading ? null : PbText.Clean(l.Unit) ?? "عدد", Standard = PbText.Clean(l.Standard), Material = PbText.Clean(l.Material),
                    RawDiameter = l.RawDiameter, RawLength = l.RawLength, RawWidth = l.RawWidth, RawThickness = l.RawThickness, NetWeight = l.NetWeight, GrossWeight = l.GrossWeight,
                    ScrapFactor = l.ScrapFactor, MakeOrBuy = l.IsHeading ? PbMakeOrBuy.Purchase : l.MakeOrBuy, Description = PbText.Clean(l.Description), ChildBomId = l.ChildBomId,
                };
                Stamp(line, true);
                h.Lines.Add(line);
            }
            return null;
        }

        private async Task LinkParentsAsync(PbBomHeader h, List<SavePbBomLineDto> lines, CancellationToken ct)
        {
            List<PbBomLine> saved = h.Lines.Where(l => !l.Deleted).OrderBy(l => l.SortOrder).ToList();
            bool any = false;
            for (int i = 0; i < lines.Count && i < saved.Count; i++)
            {
                if (lines[i].ParentIndex.HasValue) { saved[i].ParentLineId = saved[lines[i].ParentIndex.Value].Id; any = true; }
                if (lines[i].AlternativeOfIndex.HasValue) { saved[i].AlternativeOfLineId = saved[lines[i].AlternativeOfIndex.Value].Id; any = true; }
            }
            if (any) await Uow.SaveChangesAsync(ct);
        }

        // -------------------------------------------------------------------------------- the signature flow

        public async Task<ResponseDto<PbBomDto>> SubmitAsync(PbBomActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.AiNeverApproves);
            PbBomHeader h = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (h == null) return ResponseDtoHelper.NotFound<PbBomDto>(PbMessages.NotFound);
            if (h.Status != PbBomStatus.Draft) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.OnlyDraft);
            if (h.PreparedByUserId != User.UserId || !InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<PbBomDto>("پیش‌نویس را فقط تهیه‌کننده آن برای بررسی می‌فرستد.");
            List<PbBomLine> lines = LiveLines(h);
            if (!lines.Any(l => !l.IsHeading)) return ResponseDtoHelper.BadRequest<PbBomDto>("BOM هیچ قطعه‌ای ندارد.");
            if (h.Revision > 0 && PbText.Clean(h.ChangeReason) == null) return ResponseDtoHelper.BadRequest<PbBomDto>("دلیل تغییر نسخه را بنویسید.");

            string childProblem = await ChildProblemAsync(lines, ct);
            if (childProblem != null) return ResponseDtoHelper.BadRequest<PbBomDto>(childProblem);

            // every part has its item (code = part number): a part that has none gets it now, an existing item with the part's code or MESC code is adopted
            foreach (PbBomLine l in lines.Where(l => !l.IsHeading && l.ItemId == null))
            {
                var found = await FindOrCreatePartItemAsync(l.PartNumber, l.PartName, l.Unit, l.Material, l.Standard, ct);
                if (found.Error != null) return ResponseDtoHelper.BadRequest<PbBomDto>(found.Error);
                if (Uow.Entry(found.Item).State == EntityState.Added || Uow.Entry(found.Item).State == EntityState.Modified) await Uow.SaveChangesAsync(ct);   // saved at once: the next line of the same part finds it
                l.ItemId = found.Item.Id;
            }
            if (h.ProductItemId == null)
            {
                var top = await FindOrCreatePartItemAsync(h.AssemblyPartNumber, h.Title, null, null, null, ct);
                if (top.Error != null) return ResponseDtoHelper.BadRequest<PbBomDto>(top.Error);
                if (Uow.Entry(top.Item).State == EntityState.Added || Uow.Entry(top.Item).State == EntityState.Modified) await Uow.SaveChangesAsync(ct);
                h.ProductItemId = top.Item.Id;
            }
            h.PreparedAt = DateTimeOffset.Now;
            h.ReturnNote = null;
            h.ContentHash = HashOf(lines);
            h.Status = PbBomStatus.UnderReview;
            Stamp(h, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbBomDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(h, ct));
        }

        /// <summary>A BOM that is sent for review or released may name only released child BOMs (a child that is still being changed would change the parent without a new revision).</summary>
        private async Task<string> ChildProblemAsync(IEnumerable<PbBomLine> lines, CancellationToken ct)
        {
            List<int> ids = lines.Where(l => !l.Deleted && l.ChildBomId.HasValue).Select(l => l.ChildBomId.Value).Distinct().ToList();
            if (ids.Count == 0) return null;
            var heads = await Uow.Set<PbBomHeader>().AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Code, x.Revision, x.Status, x.Deleted }).ToListAsync(ct);
            var bad = heads.FirstOrDefault(x => x.Deleted || x.Status != PbBomStatus.Released);
            return bad == null && heads.Count == ids.Count ? null : "BOM زیرمجموعه" + (bad == null ? "" : " " + bad.Code + " نسخه " + PbText.Revision(bad.Revision)) + " باید منتشرشده (Released) باشد.";
        }

        public async Task<ResponseDto<PbBomDto>> ReturnAsync(PbBomActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.AiNeverApproves);
            PbBomHeader h = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (h == null) return ResponseDtoHelper.NotFound<PbBomDto>(PbMessages.NotFound);
            if (h.Status != PbBomStatus.UnderReview) return ResponseDtoHelper.BadRequest<PbBomDto>("فقط نسخه در حال بررسی برگردانده می‌شود.");
            PbBomSignature? next = NextStep(h);
            // only the person whose turn it is (the role of the next step) sends it back; the preparer never does
            if (!next.HasValue || !InRole(StepRoles(next.Value)) || h.PreparedByUserId == User.UserId) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.NotAllowed);
            if (PbText.Clean(dto.Note) == null) return ResponseDtoHelper.BadRequest<PbBomDto>("دلیل برگرداندن را بنویسید.");
            h.Status = PbBomStatus.Draft;
            h.ReturnNote = PbText.Clean(dto.Note);
            h.CheckedByUserId = null; h.CheckedAt = null;
            h.QcCheckedByUserId = null; h.QcCheckedAt = null;
            h.ManufacturingCheckedByUserId = null; h.ManufacturingCheckedAt = null;
            h.ApprovedByUserId = null; h.ApprovedAt = null;
            Stamp(h, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbBomDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(h, ct));
        }

        public async Task<ResponseDto<PbBomDto>> SignAsync(PbSignDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.AiNeverApproves);
            PbBomHeader h = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (h == null) return ResponseDtoHelper.NotFound<PbBomDto>(PbMessages.NotFound);
            if (h.Status != PbBomStatus.UnderReview) return ResponseDtoHelper.BadRequest<PbBomDto>("فقط نسخه در حال بررسی امضا می‌شود.");
            PbBomSignature? next = NextStep(h);
            if (!next.HasValue || next.Value != dto.Step) return ResponseDtoHelper.BadRequest<PbBomDto>("نوبت این امضا نیست؛ ترتیب: کنترل، کنترل کیفیت، کنترل ساخت، تایید.");
            if (!InRole(StepRoles(dto.Step))) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.NotAllowed);
            string who = SignerError(h, User.UserId, await DistinctReviewersAsync(ct));
            if (who != null) return ResponseDtoHelper.BadRequest<PbBomDto>(who);
            if (HashOf(h.Lines) != h.ContentHash) return ResponseDtoHelper.BadRequest<PbBomDto>("محتوای BOM بعد از ارسال تغییر کرده است؛ برگردانده شود و دوباره فرستاده شود.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Step)
            {
                case PbBomSignature.Checked: h.CheckedByUserId = User.UserId; h.CheckedAt = now; break;
                case PbBomSignature.QcChecked: h.QcCheckedByUserId = User.UserId; h.QcCheckedAt = now; break;
                case PbBomSignature.ManufacturingChecked: h.ManufacturingCheckedByUserId = User.UserId; h.ManufacturingCheckedAt = now; break;
                default: h.ApprovedByUserId = User.UserId; h.ApprovedAt = now; h.Status = PbBomStatus.Approved; break;
            }
            Stamp(h, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbBomDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(h, ct));
        }

        public async Task<ResponseDto<PbBomDto>> ReleaseAsync(PbBomActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.AiNeverApproves);
            PbBomHeader h = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (h == null) return ResponseDtoHelper.NotFound<PbBomDto>(PbMessages.NotFound);
            if (h.Status != PbBomStatus.Approved) return ResponseDtoHelper.BadRequest<PbBomDto>("فقط نسخه تایید‌شده منتشر می‌شود.");
            if (!InRole(PbRoles.ReleaseRoles)) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.NotAllowed);
            if (h.PreparedByUserId == User.UserId) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.SameUser);
            if (HashOf(h.Lines) != h.ContentHash) return ResponseDtoHelper.BadRequest<PbBomDto>("محتوای BOM بعد از ارسال تغییر کرده است.");
            string designProblem = await DesignGate.BomReleaseProblemAsync(Uow, h, ct);   // Package A1: closed and accepted design verification
            if (designProblem != null) return ResponseDtoHelper.BadRequest<PbBomDto>(designProblem);
            string childProblem = await ChildProblemAsync(h.Lines, ct);
            if (childProblem != null) return ResponseDtoHelper.BadRequest<PbBomDto>(childProblem);
            if (await DistinctReviewersAsync(ct) && (h.CheckedByUserId == User.UserId || h.QcCheckedByUserId == User.UserId || h.ManufacturingCheckedByUserId == User.UserId || h.ApprovedByUserId == User.UserId)) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.SameUserReviewers);
            DateTime effective = (dto.EffectiveFrom ?? DateTime.Today).Date;
            PbBomHeader previous = await Uow.Set<PbBomHeader>().FirstOrDefaultAsync(x => !x.Deleted && x.Code == h.Code && x.Status == PbBomStatus.Released && x.Id != h.Id, ct);
            if (previous != null)
            {
                if (previous.EffectiveFrom.HasValue && effective < previous.EffectiveFrom.Value.Date) return ResponseDtoHelper.BadRequest<PbBomDto>("تاریخ اجرا از تاریخ اجرای نسخه قبلی زودتر است.");
                previous.Status = PbBomStatus.Superseded;
                Stamp(previous, false);
            }
            h.Status = PbBomStatus.Released;
            h.ReleasedByUserId = User.UserId;
            h.ReleasedAt = DateTimeOffset.Now;
            h.EffectiveFrom = effective;
            Stamp(h, false);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<PbBomDto>(conflict);
            return ResponseDtoHelper.Ok(await ToDtoAsync(h, ct));
        }

        public async Task<ResponseDto<PbBomDto>> ObsoleteAsync(PbBomActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.AiNeverApproves);
            PbBomHeader h = dto == null ? null : await LoadAsync(dto.Id, true, ct);
            if (h == null) return ResponseDtoHelper.NotFound<PbBomDto>(PbMessages.NotFound);
            if (!InRole(PbRoles.ReleaseRoles)) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.NotAllowed);
            if (h.Status != PbBomStatus.Released && h.Status != PbBomStatus.Superseded) return ResponseDtoHelper.BadRequest<PbBomDto>("فقط نسخه منتشرشده یا جایگزین‌شده منسوخ می‌شود.");
            if (PbText.Clean(dto.Note) == null) return ResponseDtoHelper.BadRequest<PbBomDto>("دلیل منسوخ کردن را بنویسید.");
            h.Status = PbBomStatus.Obsolete;
            h.ChangeReason = PbText.Clean(dto.Note);
            Stamp(h, false);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(h, ct));
        }

        public async Task<ResponseDto<PbBomDto>> NewRevisionAsync(PbBomActionDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.AiNeverApproves);
            if (!InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<PbBomDto>(PbMessages.NotAllowed);
            PbBomHeader from = dto == null ? null : await LoadAsync(dto.Id, false, ct);
            if (from == null) return ResponseDtoHelper.NotFound<PbBomDto>(PbMessages.NotFound);
            if (from.Status != PbBomStatus.Released) return ResponseDtoHelper.BadRequest<PbBomDto>("نسخه جدید فقط از نسخه منتشرشده ساخته می‌شود.");
            if (PbText.Clean(dto.Note) == null) return ResponseDtoHelper.BadRequest<PbBomDto>("دلیل تغییر را بنویسید.");
            if (await HasOpenRevisionAsync(from.Code, from.Id, ct)) return ResponseDtoHelper.BadRequest<PbBomDto>("یک نسخه پیش‌نویس یا در حال بررسی از این BOM وجود دارد.");
            int revision = await Uow.Set<PbBomHeader>().Where(x => x.Code == from.Code).MaxAsync(x => x.Revision, ct) + 1;
            var h = new PbBomHeader
            {
                ProductId = from.ProductId, AssemblyPartNumber = from.AssemblyPartNumber, ProductItemId = from.ProductItemId, Title = from.Title, Code = from.Code, Revision = revision,
                Status = PbBomStatus.Draft, Platform = from.Platform, IssueFor = from.IssueFor, DocumentDate = DateTime.Today, ChangeReason = PbText.Clean(dto.Note), SupersedesId = from.Id,
                PreparedByUserId = User.UserId,
            };
            Stamp(h, true);
            Uow.Set<PbBomHeader>().Add(h);
            var map = new Dictionary<int, PbBomLine>();
            foreach (PbBomLine l in LiveLines(from))
            {
                var copy = new PbBomLine
                {
                    SortOrder = l.SortOrder, ItemNumber = l.ItemNumber, IsHeading = l.IsHeading, PartNumber = l.PartNumber, ItemId = l.ItemId, PartName = l.PartName, Qty = l.Qty, Unit = l.Unit,
                    Standard = l.Standard, Material = l.Material, RawDiameter = l.RawDiameter, RawLength = l.RawLength, RawWidth = l.RawWidth, RawThickness = l.RawThickness, NetWeight = l.NetWeight,
                    GrossWeight = l.GrossWeight, ScrapFactor = l.ScrapFactor, MakeOrBuy = l.MakeOrBuy, Description = l.Description, ChildBomId = l.ChildBomId,
                };
                Stamp(copy, true);
                h.Lines.Add(copy);
                map[l.Id] = copy;
            }
            await Uow.SaveChangesAsync(ct);
            foreach (PbBomLine l in LiveLines(from))
            {
                if (l.ParentLineId.HasValue && map.TryGetValue(l.ParentLineId.Value, out PbBomLine parent)) map[l.Id].ParentLineId = parent.Id;
                if (l.AlternativeOfLineId.HasValue && map.TryGetValue(l.AlternativeOfLineId.Value, out PbBomLine main)) map[l.Id].AlternativeOfLineId = main.Id;
            }
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(await ToDtoAsync(await LoadAsync(h.Id, false, ct), ct));
        }

        public async Task<ResponseDto<bool>> DeleteDraftAsync(int id, CancellationToken ct)
        {
            PbBomHeader h = await LoadAsync(id, true, ct);
            if (h == null) return ResponseDtoHelper.NotFound<bool>(PbMessages.NotFound);
            if (h.Status != PbBomStatus.Draft) return ResponseDtoHelper.BadRequest<bool>(PbMessages.OnlyDraft);
            if (h.PreparedByUserId != User.UserId || !InRole(PbRoles.Writers)) return ResponseDtoHelper.BadRequest<bool>(PbMessages.NotAllowed);
            if (await Uow.Set<PbBomHeader>().AnyAsync(x => x.SupersedesId == id, ct)) return ResponseDtoHelper.BadRequest<bool>("نسخه دیگری از این پیش‌نویس ساخته شده است.");
            h.Delete();
            foreach (PbBomLine l in h.Lines) l.Delete();
            Stamp(h, false);
            await Uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(true);
        }

        // -------------------------------------------------------------------------------- diff and where-used

        private static List<(string Key, PbBomLine Line, string Group)> Keyed(PbBomHeader h)
        {
            List<PbBomLine> lines = LiveLines(h);
            var heading = lines.Where(l => l.IsHeading).ToDictionary(l => l.Id, l => l.PartName);
            var count = new Dictionary<string, int>();
            var list = new List<(string, PbBomLine, string)>();
            foreach (PbBomLine l in lines.Where(l => !l.IsHeading))
            {
                count.TryGetValue(l.PartNumber ?? "", out int n);
                count[l.PartNumber ?? ""] = n + 1;
                string group = l.ParentLineId.HasValue && heading.TryGetValue(l.ParentLineId.Value, out string g) ? g : null;
                list.Add(((l.PartNumber ?? "") + "#" + (n + 1), l, group));
            }
            return list;
        }

        private static string AltOf(PbBomHeader h, PbBomLine l) => l.AlternativeOfLineId.HasValue ? h.Lines.FirstOrDefault(x => x.Id == l.AlternativeOfLineId.Value)?.PartNumber : null;

        private static PbDiffLineDto DiffLine(PbBomLine l) => new PbDiffLineDto { PartNumber = l.PartNumber, PartName = l.PartName, ItemNumber = l.ItemNumber, Qty = l.Qty };

        public async Task<ResponseDto<PbBomDiffDto>> DiffAsync(int fromId, int toId, CancellationToken ct)
        {
            PbBomHeader a = await LoadAsync(fromId, false, ct);
            PbBomHeader b = await LoadAsync(toId, false, ct);
            if (a == null || b == null) return ResponseDtoHelper.NotFound<PbBomDiffDto>(PbMessages.NotFound);
            var result = new PbBomDiffDto { FromId = a.Id, ToId = b.Id, FromCode = a.Code + " / " + PbText.Revision(a.Revision), ToCode = b.Code + " / " + PbText.Revision(b.Revision) };
            var ka = Keyed(a).ToDictionary(x => x.Key);
            var kb = Keyed(b).ToDictionary(x => x.Key);
            foreach (var x in kb.Values.OrderBy(v => v.Line.SortOrder))
            {
                if (!ka.TryGetValue(x.Key, out var old)) { result.Added.Add(DiffLine(x.Line)); continue; }
                var fields = new List<PbDiffFieldDto>();
                void F(string name, string o, string n) { if ((o ?? "") != (n ?? "")) fields.Add(new PbDiffFieldDto { Field = name, Old = o, New = n }); }
                PbBomLine p = old.Line, q = x.Line;
                F("PartName", p.PartName, q.PartName); F("Qty", PbText.Num(p.Qty), PbText.Num(q.Qty)); F("Unit", p.Unit, q.Unit); F("Standard", p.Standard, q.Standard); F("Material", p.Material, q.Material);
                F("RawDiameter", PbText.Num(p.RawDiameter), PbText.Num(q.RawDiameter)); F("RawLength", PbText.Num(p.RawLength), PbText.Num(q.RawLength));
                F("RawWidth", PbText.Num(p.RawWidth), PbText.Num(q.RawWidth)); F("RawThickness", PbText.Num(p.RawThickness), PbText.Num(q.RawThickness));
                F("NetWeight", PbText.Num(p.NetWeight), PbText.Num(q.NetWeight)); F("GrossWeight", PbText.Num(p.GrossWeight), PbText.Num(q.GrossWeight));
                F("ScrapFactor", PbText.Num(p.ScrapFactor), PbText.Num(q.ScrapFactor)); F("MakeOrBuy", p.MakeOrBuy.ToString(), q.MakeOrBuy.ToString());
                F("Description", p.Description, q.Description); F("Group", old.Group, x.Group); F("ChildBomId", p.ChildBomId?.ToString(), q.ChildBomId?.ToString());
                F("ItemNumber", p.ItemNumber?.ToString(), q.ItemNumber?.ToString()); F("AlternativeOf", AltOf(a, p), AltOf(b, q));
                if (fields.Count == 0) { result.Unchanged++; continue; }
                PbDiffLineDto d = DiffLine(q);
                d.Fields = fields;
                result.Changed.Add(d);
            }
            foreach (var x in ka.Values.OrderBy(v => v.Line.SortOrder))
                if (!kb.ContainsKey(x.Key)) result.Removed.Add(DiffLine(x.Line));
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<PbWhereUsedDto>> WhereUsedAsync(string partNumber, CancellationToken ct)
        {
            string pn = PbText.PartNumber(partNumber);
            if (pn == null) return ResponseDtoHelper.BadRequest<PbWhereUsedDto>(PbMessages.PartNumberRequired);
            var rows = await (from l in Uow.Set<PbBomLine>().AsNoTracking()
                              join h in Uow.Set<PbBomHeader>().AsNoTracking() on l.BomHeaderId equals h.Id
                              where !l.Deleted && !h.Deleted && l.PartNumber == pn
                              orderby h.Code, h.Revision descending
                              select new { h.Id, h.Code, h.Revision, h.Status, h.AssemblyPartNumber, l.ItemNumber, l.Qty, l.PartName }).ToListAsync(ct);
            var dto = new PbWhereUsedDto { PartNumber = pn, PartName = rows.Select(r => r.PartName).FirstOrDefault() ?? (await Uow.Set<InventoryItem>().AsNoTracking().Where(i => i.PartNumber == pn && i.StageCode2 == null).Select(i => i.Title).FirstOrDefaultAsync(ct)) };
            foreach (var r in rows)
                dto.Rows.Add(new PbWhereUsedRowDto { BomId = r.Id, BomCode = r.Code, Revision = r.Revision, Status = r.Status, AssemblyPartNumber = r.AssemblyPartNumber, ItemNumber = r.ItemNumber, Qty = r.Qty, InLatestRelease = r.Status == PbBomStatus.Released });
            return ResponseDtoHelper.Ok(dto);
        }
    }
}
