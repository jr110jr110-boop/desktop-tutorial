using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Identity;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IEdmsService
    {
        // revisions (E4, E5, E9)
        Task<ResponseDto<PcDeliverableHistoryDto>> GetHistoryAsync(int deliverableId, CancellationToken cancellationToken);
        Task<ResponseDto<PcRevisionDto>> GetRevisionAsync(int revisionId, CancellationToken cancellationToken);
        Task<ResponseDto<PcRevisionDto>> CreateRevisionAsync(PcRevisionCreateDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcRevisionDto>> AddFileAsync(PcRevisionFileAddDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcRevisionDto>> RemoveFileAsync(PcRevisionFileRemoveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcRevisionDto>> ActionAsync(PcRevisionActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcRevisionCompareDto>> CompareAsync(int revisionA, int revisionB, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcRevisionDto>>> RevisionsOfAsync(string kind, int id, CancellationToken cancellationToken);

        // check-out
        Task<ResponseDto<PcDeliverableHistoryDto>> CheckOutAsync(PcCheckOutDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcDeliverableHistoryDto>> CheckInAsync(PcCheckOutDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcDeliverableHistoryDto>> CancelCheckOutAsync(PcCheckOutDto dto, CancellationToken cancellationToken);

        // links and tags (E12, P3)
        Task<ResponseDto<PcDocLinkDto>> SaveLinkAsync(PcDocLinkSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<bool>> VoidLinkAsync(PcDocLinkVoidDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcDocLinkDto>>> GetLinksAsync(PcDocLinkQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcPartDocumentDto>>> GetPartDocumentsAsync(PcDocLinkQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcTagDto>>> GetTagsAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcTagDto>> SaveTagAsync(PcTagSaveDto dto, CancellationToken cancellationToken);

        // distribution, matrix, notices (E6, E14)
        Task<ResponseDto<List<PcDistributionDto>>> GetDistributionAsync(CancellationToken cancellationToken);
        Task<ResponseDto<PcDistributionDto>> SaveDistributionAsync(PcDistributionSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcApproverDto>>> GetApproversAsync(CancellationToken cancellationToken);
        Task<ResponseDto<PcApproverDto>> SaveApproverAsync(PcApproverSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcNoticeDto>>> GetNoticesAsync(PcDocNoticeQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcNoticeDto>> AcknowledgeAsync(int noticeId, CancellationToken cancellationToken);

        // shop floor (E6, E11)
        Task<ResponseDto<List<PcShopDocumentDto>>> GetShopDocumentsAsync(int orderId, int? operationId, CancellationToken cancellationToken);
        Task<ResponseDto<PcControlledCopyDto>> ControlledCopyAsync(string source, int revisionId, Guid? documentId, bool print, CancellationToken cancellationToken);

        // import / export (item 8)
        Task<ResponseDto<byte[]>> ExportMdrAsync(int contractId, bool clientOnly, int? workOrderId, bool templateOnly, CancellationToken cancellationToken);
        Task<ResponseDto<PcMdrImportResultDto>> ImportMdrAsync(int contractId, byte[] file, CancellationToken cancellationToken);

        // tender register (E1, E2)
        Task<ResponseDto<PagedList<PcTenderPackageDto>>> GetTenderPackagesAsync(PcTenderQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcTenderPackageDto>> GetTenderPackageAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcTenderPackageDto>> SaveTenderPackageAsync(PcTenderPackageSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcTenderPackageDto>> SaveTenderDocumentAsync(PcTenderDocumentSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcTenderPackageDto>> TenderActionAsync(PcTenderActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcTenderPackageDto>>> TendersOfContractAsync(int contractId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Engineering document control: the revision lifecycle of the MDR / MDL deliverables, check-out, links to parts and tags, distribution with acknowledgement, the discipline approver matrix, the shop-floor
    /// viewer and the tender register. Maker-checker everywhere: the preparer of a revision never reviews, approves or releases it; an AI seat never reviews, approves, rejects, releases or retires one.
    /// </summary>
    public partial class EdmsService : IEdmsService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public EdmsService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);
        private static bool IsAi => global::Services.NasbNiro.Ai.AiActing.IsActing;

        private const string AiRefusal = "یک صندلی هوش مصنوعی بررسی، تایید یا انتشار سند را انجام نمی‌دهد؛ این کار با انسان است.";
        private const string LockedBy = "سند برای ویرایش توسط شخص دیگری برداشته شده است (check-out).";

        private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int?> ids, CancellationToken ct)
        {
            List<int> list = ids.Where(i => i.HasValue).Select(i => i.Value).Distinct().ToList();
            if (list.Count == 0) return new Dictionary<int, string>();
            var rows = await _uow.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.LastName, u.UserName }).ToListAsync(ct);
            return rows.ToDictionary(u => u.Id, u => { string n = ((u.Name ?? "") + " " + (u.LastName ?? "")).Trim(); return n.Length > 0 ? n : u.UserName; });
        }

        private static string NameOf(Dictionary<int, string> names, int? id) => id.HasValue && names.TryGetValue(id.Value, out string n) ? n : null;

        private void Log(int deliverableId, int? revisionId, PcDocEventKind kind, string note, DateTimeOffset? at = null) =>
            _uow.Set<PcDocumentEvent>().Add(new PcDocumentEvent { DeliverableId = deliverableId, RevisionId = revisionId, Kind = kind, UserId = Me, At = at ?? DateTimeOffset.Now, Note = Clean(note) });

        // ============================================================================ permissions (the approver matrix of the discipline, else the roles)

        private sealed class Perms
        {
            public bool CanPrepare, CanReview, CanApprove, CanRelease, IsManager;
        }

        private async Task<Perms> PermsAsync(string discipline, int contractId, CancellationToken ct)
        {
            string disc = (Clean(discipline) ?? "").ToUpperInvariant();
            var rows = await _uow.Set<PcDisciplineApprover>().AsNoTracking().Where(a => !a.Deleted && a.IsActive && (a.ContractId == null || a.ContractId == contractId)).ToListAsync(ct);
            rows = rows.Where(a => string.Equals((Clean(a.Discipline) ?? "").ToUpperInvariant(), disc, StringComparison.Ordinal)).ToList();
            bool anyReviewer = rows.Any(a => a.CanReview), anyApprover = rows.Any(a => a.CanApprove);
            bool mineReviewer = rows.Any(a => a.CanReview && a.UserId == Me), mineApprover = rows.Any(a => a.CanApprove && a.UserId == Me);
            bool pmo = _user.IsInRole(ProjectControlRoles.Pmo);
            return new Perms
            {
                CanPrepare = _user.IsInRole(EdmsRoles.Preparers) || mineReviewer || mineApprover,
                CanReview = anyReviewer ? (mineReviewer || mineApprover || pmo) : _user.IsInRole(EdmsRoles.FallbackReviewers),
                CanApprove = anyApprover ? (mineApprover || pmo) : _user.IsInRole(EdmsRoles.FallbackApprovers),
                CanRelease = mineApprover || _user.IsInRole(EdmsRoles.FallbackReleasers),
                IsManager = _user.IsInRole(EdmsRoles.Managers),
            };
        }

        /// <summary>The actions the caller may take on a revision now (the same rules the service enforces).</summary>
        private List<string> ActionsOf(PcDocumentRevision r, PcEngineeringDeliverable d, Perms p, bool reviewRequired, bool hasFiles)
        {
            var a = new List<string>();
            bool lockedByOther = d.CheckedOutByUserId.HasValue && d.CheckedOutByUserId != Me;
            bool notPreparer = r.PreparedByUserId != Me;
            switch (r.Status)
            {
                case PcDocStatus.Draft:
                    if (p.CanPrepare && !lockedByOther && !d.ReceivedFromClient) { a.Add("add-file"); a.Add("remove-file"); a.Add("link-change"); if (hasFiles) a.Add("submit"); }
                    break;
                case PcDocStatus.UnderReview:
                    if (!IsAi)
                    {
                        if (p.CanReview && notPreparer && r.ReviewerUserId == null) a.Add("review");
                        if (p.CanApprove && notPreparer && r.ReviewerUserId != Me && (r.ReviewerUserId != null || !reviewRequired)) a.Add("approve");
                        if ((p.CanReview || p.CanApprove) && notPreparer) a.Add("reject");
                    }
                    break;
                case PcDocStatus.Approved:
                    if (!IsAi)
                    {
                        if (p.CanRelease && notPreparer) a.Add("release");
                        if ((p.CanReview || p.CanApprove) && notPreparer) a.Add("reject");
                    }
                    break;
                case PcDocStatus.Released:
                    if (!IsAi && p.CanRelease && notPreparer) { a.Add("as-built"); a.Add("obsolete"); }
                    break;
            }
            if (r.Status != PcDocStatus.Superseded && p.CanPrepare) a.Add("link-quality");
            return a;
        }

        // ============================================================================ DTOs

        private async Task<List<PcRevisionDto>> BuildAsync(List<PcDocumentRevision> revs, PcEngineeringDeliverable d, CancellationToken ct)
        {
            if (revs.Count == 0) return new List<PcRevisionDto>();
            List<int> ids = revs.Select(r => r.Id).ToList();
            List<PcDocumentRevisionFile> files = await _uow.Set<PcDocumentRevisionFile>().AsNoTracking().Where(f => !f.Deleted && ids.Contains(f.RevisionId)).OrderBy(f => f.Id).ToListAsync(ct);
            List<PcDocumentQualityLink> qlinks = await _uow.Set<PcDocumentQualityLink>().AsNoTracking().Where(q => !q.Deleted && ids.Contains(q.RevisionId)).OrderBy(q => q.Id).ToListAsync(ct);
            List<int> ncrIds = qlinks.Where(q => q.Kind == PcQualityLinkKind.Ncr).Select(q => q.TargetId).ToList();
            List<int> qcIds = qlinks.Where(q => q.Kind == PcQualityLinkKind.QcRecord).Select(q => q.TargetId).ToList();
            Dictionary<int, string> ncrNumbers = ncrIds.Count == 0 ? new Dictionary<int, string>() : (await _uow.Set<NonConformance>().AsNoTracking().Where(n => ncrIds.Contains(n.Id)).Select(n => new { n.Id, n.Number }).ToListAsync(ct)).ToDictionary(n => n.Id, n => n.Number);
            Dictionary<int, string> qcNumbers = qcIds.Count == 0 ? new Dictionary<int, string>() : (await _uow.Set<QcRecord>().AsNoTracking().Where(n => qcIds.Contains(n.Id)).Select(n => new { n.Id, n.Number }).ToListAsync(ct)).ToDictionary(n => n.Id, n => n.Number);
            List<int> crIds = revs.Where(r => r.ChangeRequestId.HasValue).Select(r => r.ChangeRequestId.Value).Distinct().ToList();
            Dictionary<int, string> crNumbers = crIds.Count == 0 ? new Dictionary<int, string>() : (await _uow.Set<PcChangeRequest>().AsNoTracking().Where(c => crIds.Contains(c.Id)).Select(c => new { c.Id, c.Number }).ToListAsync(ct)).ToDictionary(c => c.Id, c => c.Number);
            Dictionary<int, string> names = await NamesAsync(revs.SelectMany(r => new int?[] { r.PreparedByUserId, r.ReviewerUserId, r.ApproverUserId, r.ReleasedByUserId }), ct);
            Perms perms = await PermsAsync(d.Discipline, d.ContractId, ct);
            bool reviewRequired = (await PcSettingKeys.LoadAsync(_uow, ct))[PcSettingKeys.EdmsReviewRequired] >= 1m;

            return revs.OrderBy(r => r.Sequence).Select(r => new PcRevisionDto
            {
                Id = r.Id, DeliverableId = r.DeliverableId, DocumentNumber = d.DocumentNumber, Title = d.Title, Discipline = d.Discipline, Sequence = r.Sequence, Code = r.Code, Status = r.Status, Reason = r.Reason,
                PreparedByUserId = r.PreparedByUserId, PreparedByName = NameOf(names, r.PreparedByUserId), CreatedAt = r.CreateDate, SubmittedAt = r.SubmittedAt,
                ReviewerUserId = r.ReviewerUserId, ReviewerName = NameOf(names, r.ReviewerUserId), ReviewedAt = r.ReviewedAt, ReviewNote = r.ReviewNote,
                ApproverUserId = r.ApproverUserId, ApproverName = NameOf(names, r.ApproverUserId), ApprovedAt = r.ApprovedAt,
                ReleasedByUserId = r.ReleasedByUserId, ReleasedByName = NameOf(names, r.ReleasedByUserId), ReleasedAt = r.ReleasedAt,
                RejectReason = r.RejectReason, RejectedAt = r.RejectedAt, SupersededByRevisionId = r.SupersededByRevisionId, SupersededAt = r.SupersededAt, ObsoleteReason = r.ObsoleteReason,
                ChangeImpact = r.ChangeImpact, ChangeRequestId = r.ChangeRequestId, ChangeRequestNumber = r.ChangeRequestId.HasValue && crNumbers.TryGetValue(r.ChangeRequestId.Value, out string cn) ? cn : null,
                ReceivedFromClient = r.ReceivedFromClient,
                Files = files.Where(f => f.RevisionId == r.Id).Select(f => new PcRevisionFileDto { Id = f.Id, DocumentId = f.DocumentId, Role = f.Role, FileName = f.FileName, Title = f.Title }).ToList(),
                QualityLinks = qlinks.Where(q => q.RevisionId == r.Id).Select(q => new PcRevisionQualityLinkDto
                    { Id = q.Id, Kind = q.Kind, TargetId = q.TargetId, Note = q.Note, TargetNumber = q.Kind == PcQualityLinkKind.Ncr ? (ncrNumbers.TryGetValue(q.TargetId, out string a) ? a : null) : (qcNumbers.TryGetValue(q.TargetId, out string b) ? b : null) }).ToList(),
                Actions = ActionsOf(r, d, perms, reviewRequired, files.Any(f => f.RevisionId == r.Id)),
                RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
            }).ToList();
        }

        private async Task<ResponseDto<PcRevisionDto>> RevisionResultAsync(PcDocumentRevision r, CancellationToken ct)
        {
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().FirstAsync(x => x.Id == r.DeliverableId, ct);
            return ResponseDtoHelper.Ok((await BuildAsync(new List<PcDocumentRevision> { r }, d, ct)).First());
        }

        private async Task<string> SchemeAsync(CancellationToken ct) =>
            (await PcSettingKeys.LoadAsync(_uow, ct))[PcSettingKeys.EdmsRevisionScheme] == EdmsRules.SchemeAlpha ? "alpha" : "numeric";

        public async Task<ResponseDto<PcDeliverableHistoryDto>> GetHistoryAsync(int deliverableId, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.Readers)) return Bad<PcDeliverableHistoryDto>(PcText.NotAllowed);
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == deliverableId && !x.Deleted, ct);
            if (d == null) return PcText.NotFound<PcDeliverableHistoryDto>("سند پیدا نشد.");
            return ResponseDtoHelper.Ok(await HistoryAsync(d, ct));
        }

        private async Task<PcDeliverableHistoryDto> HistoryAsync(PcEngineeringDeliverable d, CancellationToken ct)
        {
            List<PcDocumentRevision> revs = await _uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted && r.DeliverableId == d.Id).ToListAsync(ct);
            List<PcDocumentEvent> events = await _uow.Set<PcDocumentEvent>().AsNoTracking().Where(e => e.DeliverableId == d.Id).OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(300).ToListAsync(ct);
            Dictionary<int, string> names = await NamesAsync(events.Select(e => (int?)e.UserId).Concat(new int?[] { d.CheckedOutByUserId }), ct);
            Perms p = await PermsAsync(d.Discipline, d.ContractId, ct);
            var dto = new PcDeliverableHistoryDto
            {
                DeliverableId = d.Id, DocumentNumber = d.DocumentNumber, Title = d.Title, Discipline = d.Discipline, RevisionScheme = await SchemeAsync(ct),
                CheckedOutByUserId = d.CheckedOutByUserId, CheckedOutByName = NameOf(names, d.CheckedOutByUserId), CheckedOutAt = d.CheckedOutAt, CheckOutNote = d.CheckOutNote,
                Revisions = await BuildAsync(revs, d, ct),
                Events = events.Select(e => new PcDocumentEventDto { Kind = e.Kind, UserId = e.UserId, UserName = NameOf(names, e.UserId), At = e.At, RevisionId = e.RevisionId, Note = e.Note }).ToList(),
                Links = await LinksOfDeliverableAsync(d.Id, ct),
            };
            bool open = revs.Any(r => EdmsRules.IsOpen(r.Status));
            if (p.CanPrepare && !d.ReceivedFromClient)
            {
                if (!open) dto.DeliverableActions.Add("new-revision");
                if (!d.CheckedOutByUserId.HasValue) dto.DeliverableActions.Add("check-out");
                else if (d.CheckedOutByUserId == Me) dto.DeliverableActions.Add("check-in");
            }
            if (d.CheckedOutByUserId.HasValue && d.CheckedOutByUserId != Me && p.IsManager) dto.DeliverableActions.Add("cancel-check-out");
            return dto;
        }

        public async Task<ResponseDto<PcRevisionDto>> GetRevisionAsync(int revisionId, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.Readers)) return Bad<PcRevisionDto>(PcText.NotAllowed);
            PcDocumentRevision r = await _uow.Set<PcDocumentRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == revisionId && !x.Deleted, ct);
            return r == null ? PcText.NotFound<PcRevisionDto>("بازنگری پیدا نشد.") : await RevisionResultAsync(r, ct);
        }

        // ============================================================================ create, files, actions

        public async Task<ResponseDto<PcRevisionDto>> CreateRevisionAsync(PcRevisionCreateDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcRevisionDto>(PcText.InvalidData);
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().FirstOrDefaultAsync(x => x.Id == dto.DeliverableId && !x.Deleted, ct);
            if (d == null) return PcText.NotFound<PcRevisionDto>("سند پیدا نشد.");
            Perms p = await PermsAsync(d.Discipline, d.ContractId, ct);
            if (!p.CanPrepare) return Bad<PcRevisionDto>(PcText.NotAllowed);
            if (d.ReceivedFromClient) return Bad<PcRevisionDto>("سند دریافت‌شده از کارفرما با ثبت دریافت جدید به‌روز می‌شود، نه با بازنگری داخلی.");
            if (d.CheckedOutByUserId.HasValue && d.CheckedOutByUserId != Me) return Bad<PcRevisionDto>(LockedBy);
            List<PcDocumentRevision> all = await _uow.Set<PcDocumentRevision>().Where(r => r.DeliverableId == d.Id).ToListAsync(ct);
            PcDocumentRevision open = all.FirstOrDefault(r => !r.Deleted && EdmsRules.IsOpen(r.Status));
            if (open != null) return Bad<PcRevisionDto>($"بازنگری {open.Code} هنوز باز است؛ آن را منتشر یا رد کنید و بعد بازنگری تازه بسازید.");
            string reason = Clean(dto.Reason);
            if (all.Count > 0 && (reason == null || reason.Length < 3)) return Bad<PcRevisionDto>("علت بازنگری تازه را بنویسید (حداقل ۳ حرف).");
            if (dto.ChangeRequestId.HasValue)
            {
                string err = await ChangeRequestErrorAsync(dto.ChangeRequestId.Value, d.ContractId, ct);
                if (err != null) return Bad<PcRevisionDto>(err);
            }
            int scheme = (int)(await PcSettingKeys.LoadAsync(_uow, ct))[PcSettingKeys.EdmsRevisionScheme];
            int seq = (all.Count == 0 ? 0 : all.Max(r => r.Sequence)) + 1;
            DateTimeOffset now = DateTimeOffset.Now;
            var r2 = new PcDocumentRevision
            {
                DeliverableId = d.Id, Sequence = seq, Code = EdmsRules.NextCode(scheme, seq), Status = PcDocStatus.Draft, Reason = reason ?? "صدور اولیه", PreparedByUserId = Me,
                ChangeImpact = dto.ChangeImpact || dto.ChangeRequestId.HasValue, ChangeRequestId = dto.ChangeRequestId, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            };
            _uow.Set<PcDocumentRevision>().Add(r2);
            await _uow.SaveChangesAsync(ct);
            Log(d.Id, r2.Id, PcDocEventKind.RevisionCreated, $"{r2.Code}: {r2.Reason}", now);
            await _uow.SaveChangesAsync(ct);
            return await RevisionResultAsync(r2, ct);
        }

        private async Task<string> ChangeRequestErrorAsync(int changeId, int contractId, CancellationToken ct)
        {
            var c = await _uow.Set<PcChangeRequest>().AsNoTracking().Where(x => x.Id == changeId && !x.Deleted).Select(x => new { x.ContractId, x.Status }).FirstOrDefaultAsync(ct);
            if (c == null) return "درخواست تغییر پیدا نشد.";
            if (c.ContractId != contractId) return "درخواست تغییر متعلق به قرارداد این سند نیست.";
            if (c.Status == PcChangeStatus.Rejected) return "درخواست تغییر رد شده است.";
            return null;
        }

        private async Task<(PcDocumentRevision Rev, PcEngineeringDeliverable Doc, string Error)> LoadEditableAsync(int revisionId, CancellationToken ct)
        {
            PcDocumentRevision r = await _uow.Set<PcDocumentRevision>().FirstOrDefaultAsync(x => x.Id == revisionId && !x.Deleted, ct);
            if (r == null) return (null, null, "بازنگری پیدا نشد.");
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().FirstAsync(x => x.Id == r.DeliverableId, ct);
            Perms p = await PermsAsync(d.Discipline, d.ContractId, ct);
            if (!p.CanPrepare) return (r, d, PcText.NotAllowed);
            if (!EdmsRules.IsEditable(r.Status)) return (r, d, "بازنگری پس از ارسال برای بررسی (و پس از انتشار) تغییر نمی‌کند؛ بازنگری تازه بسازید.");
            if (d.CheckedOutByUserId.HasValue && d.CheckedOutByUserId != Me) return (r, d, LockedBy);
            return (r, d, null);
        }

        public async Task<ResponseDto<PcRevisionDto>> AddFileAsync(PcRevisionFileAddDto dto, CancellationToken ct)
        {
            if (dto == null || dto.DocumentId == Guid.Empty) return Bad<PcRevisionDto>(PcText.InvalidData);
            var (r, d, err) = await LoadEditableAsync(dto.RevisionId, ct);
            if (err != null) return Bad<PcRevisionDto>(err);
            if (!Enum.IsDefined(typeof(PcRevisionFileRole), dto.Role)) return Bad<PcRevisionDto>("نقش فایل نامعتبر است.");
            if (!await _uow.Set<Document>().AnyAsync(x => x.Id == dto.DocumentId, ct)) return Bad<PcRevisionDto>("فایل پیدا نشد؛ ابتدا آن را بارگذاری کنید.");
            if (await _uow.Set<PcDocumentRevisionFile>().AnyAsync(f => !f.Deleted && f.RevisionId == r.Id && f.DocumentId == dto.DocumentId, ct)) return Bad<PcRevisionDto>("این فایل قبلاً به بازنگری افزوده شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            _uow.Set<PcDocumentRevisionFile>().Add(new PcDocumentRevisionFile
                { RevisionId = r.Id, DocumentId = dto.DocumentId, Role = dto.Role, FileName = Clean(dto.FileName), Title = Clean(dto.Title), CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me });
            r.ModifyDate = now; r.ModifierUserId = Me;
            Log(d.Id, r.Id, PcDocEventKind.FileAdded, Clean(dto.FileName), now);
            await _uow.SaveChangesAsync(ct);
            return await RevisionResultAsync(r, ct);
        }

        public async Task<ResponseDto<PcRevisionDto>> RemoveFileAsync(PcRevisionFileRemoveDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<PcRevisionDto>(PcText.InvalidData);
            PcDocumentRevisionFile f = await _uow.Set<PcDocumentRevisionFile>().FirstOrDefaultAsync(x => x.Id == dto.FileId && !x.Deleted, ct);
            if (f == null) return PcText.NotFound<PcRevisionDto>("فایل پیدا نشد.");
            var (r, d, err) = await LoadEditableAsync(f.RevisionId, ct);
            if (err != null) return Bad<PcRevisionDto>(err);
            string reason = Clean(dto.Reason);
            if (reason == null || reason.Length < 3) return Bad<PcRevisionDto>("علت حذف فایل را بنویسید (حداقل ۳ حرف).");
            DateTimeOffset now = DateTimeOffset.Now;
            f.Delete(); f.RemoveReason = reason; f.ModifyDate = now; f.ModifierUserId = Me;
            Log(d.Id, r.Id, PcDocEventKind.FileAdded, "حذف فایل: " + reason, now);
            await _uow.SaveChangesAsync(ct);
            return await RevisionResultAsync(r, ct);
        }

        public async Task<ResponseDto<PcRevisionDto>> ActionAsync(PcRevisionActionDto dto, CancellationToken ct)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcRevisionDto>(PcText.InvalidData);
            PcDocumentRevision r = await _uow.Set<PcDocumentRevision>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return PcText.NotFound<PcRevisionDto>("بازنگری پیدا نشد.");
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().FirstAsync(x => x.Id == r.DeliverableId, ct);
            Perms p = await PermsAsync(d.Discipline, d.ContractId, ct);
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, ct);
            bool reviewRequired = settings[PcSettingKeys.EdmsReviewRequired] >= 1m;
            string action = dto.Action.Trim().ToLowerInvariant();
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            bool isReviewStep = action == "review" || action == "approve" || action == "reject" || action == "release" || action == "as-built" || action == "obsolete";
            if (isReviewStep && IsAi) return Bad<PcRevisionDto>(AiRefusal);

            switch (action)
            {
                case "submit":
                {
                    if (!p.CanPrepare) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (r.Status != PcDocStatus.Draft) return Bad<PcRevisionDto>("فقط پیش‌نویس برای بررسی ارسال می‌شود.");
                    if (d.CheckedOutByUserId.HasValue && d.CheckedOutByUserId != Me) return Bad<PcRevisionDto>(LockedBy);
                    if (!await _uow.Set<PcDocumentRevisionFile>().AnyAsync(f => !f.Deleted && f.RevisionId == r.Id, ct)) return Bad<PcRevisionDto>("بازنگری فایلی ندارد؛ ابتدا فایل سند را بیفزایید.");
                    if (r.ChangeImpact && !r.ChangeRequestId.HasValue) return Bad<PcRevisionDto>("این بازنگری زمان، هزینه یا مواد را تغییر می‌دهد؛ ابتدا آن را به یک درخواست تغییر وصل کنید (مدیریت تغییر).");
                    r.Status = PcDocStatus.UnderReview; r.SubmittedAt = now; r.ReviewerUserId = null; r.ReviewedAt = null; r.ReviewNote = null; r.RejectReason = null;
                    if (d.IssueState == PcIssueState.NotIssued) { d.IssueState = PcIssueState.FirstIssue; d.IssuedByUserId = Me; }
                    d.ModifyDate = now; d.ModifierUserId = Me;
                    Log(d.Id, r.Id, PcDocEventKind.Submitted, note, now);
                    break;
                }
                case "review":
                {
                    if (r.Status != PcDocStatus.UnderReview) return Bad<PcRevisionDto>("بازنگری در مرحله بررسی نیست.");
                    if (!p.CanReview) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (r.PreparedByUserId == Me) return Bad<PcRevisionDto>(PcText.SameUser);
                    if (r.ReviewerUserId.HasValue) return Bad<PcRevisionDto>("بازنگری قبلاً بررسی شده است.");
                    r.ReviewerUserId = Me; r.ReviewedAt = now; r.ReviewNote = note;
                    Log(d.Id, r.Id, PcDocEventKind.Reviewed, note, now);
                    break;
                }
                case "approve":
                {
                    if (r.Status != PcDocStatus.UnderReview) return Bad<PcRevisionDto>("بازنگری در مرحله بررسی نیست.");
                    if (!p.CanApprove) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (r.PreparedByUserId == Me || r.ReviewerUserId == Me) return Bad<PcRevisionDto>(PcText.SameUser + " (تهیه‌کننده، بررسی‌کننده و تاییدکننده سه نفر جدا هستند.)");
                    if (reviewRequired && !r.ReviewerUserId.HasValue) return Bad<PcRevisionDto>("پیش از تایید، نفر دوم باید بازنگری را بررسی کند.");
                    r.Status = PcDocStatus.Approved; r.ApproverUserId = Me; r.ApprovedAt = now;
                    Log(d.Id, r.Id, PcDocEventKind.Approved, note, now);
                    break;
                }
                case "reject":
                {
                    if (r.Status != PcDocStatus.UnderReview && r.Status != PcDocStatus.Approved) return Bad<PcRevisionDto>("فقط بازنگریِ در حال بررسی یا تاییدشده رد می‌شود.");
                    if (!(p.CanReview || p.CanApprove)) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (r.PreparedByUserId == Me) return Bad<PcRevisionDto>(PcText.SameUser);
                    if (note == null || note.Length < 3) return Bad<PcRevisionDto>("علت رد الزامی است (حداقل ۳ حرف).");
                    r.Status = PcDocStatus.Draft; r.RejectedByUserId = Me; r.RejectedAt = now; r.RejectReason = note;
                    r.ReviewerUserId = null; r.ReviewedAt = null; r.ApproverUserId = null; r.ApprovedAt = null; r.SubmittedAt = null;
                    Log(d.Id, r.Id, PcDocEventKind.Rejected, note, now);
                    break;
                }
                case "release":
                {
                    if (r.Status != PcDocStatus.Approved) return Bad<PcRevisionDto>("فقط بازنگری تاییدشده منتشر می‌شود.");
                    if (!p.CanRelease) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (r.PreparedByUserId == Me) return Bad<PcRevisionDto>(PcText.SameUser);
                    if (r.ChangeImpact)
                    {
                        var c = r.ChangeRequestId.HasValue ? await _uow.Set<PcChangeRequest>().AsNoTracking().Where(x => x.Id == r.ChangeRequestId.Value).Select(x => new { x.Status, x.Decision }).FirstOrDefaultAsync(ct) : null;
                        if (c == null) return Bad<PcRevisionDto>("بازنگری زمان، هزینه یا مواد را تغییر می‌دهد و درخواست تغییر ندارد.");
                        bool approved = c.Decision == PcChangeDecision.Approve && (c.Status == PcChangeStatus.Decided || c.Status == PcChangeStatus.Implementing || c.Status == PcChangeStatus.Closed);
                        if (!approved) return Bad<PcRevisionDto>("درخواست تغییر این بازنگری هنوز تایید نشده است؛ پس از تصمیم هیئت تغییر منتشر کنید.");
                    }
                    List<PcDocumentRevisionFile> files = await _uow.Set<PcDocumentRevisionFile>().Where(f => !f.Deleted && f.RevisionId == r.Id).OrderBy(f => f.Id).ToListAsync(ct);
                    if (files.Count == 0) return Bad<PcRevisionDto>("بازنگری فایلی ندارد.");
                    List<PcDocumentRevision> previous = await _uow.Set<PcDocumentRevision>().Where(x => x.DeliverableId == d.Id && x.Id != r.Id && !x.Deleted && x.Status == PcDocStatus.Released).ToListAsync(ct);
                    r.Status = PcDocStatus.Released; r.ReleasedByUserId = Me; r.ReleasedAt = now;
                    foreach (PcDocumentRevision old in previous) { old.Status = PcDocStatus.Superseded; old.SupersededAt = now; old.ModifyDate = now; old.ModifierUserId = Me; Log(d.Id, old.Id, PcDocEventKind.Superseded, "جایگزین‌شده با " + r.Code, now); }
                    PcDocumentRevisionFile primary = files.FirstOrDefault(f => f.Role == PcRevisionFileRole.Pdf) ?? files.FirstOrDefault(f => f.Role == PcRevisionFileRole.Native) ?? files[0];
                    d.Revision = r.Code; d.DocumentId = primary.DocumentId;
                    if (!d.ClientSubmittal)
                    {
                        d.IssueState = PcIssueState.Approved; d.ApprovedAt = now; d.ApprovedByUserId = r.ApproverUserId ?? Me;
                        if (!d.IssuedByUserId.HasValue) d.IssuedByUserId = r.PreparedByUserId;
                    }
                    d.ModifyDate = now; d.ModifierUserId = Me;
                    Log(d.Id, r.Id, PcDocEventKind.Released, note, now);
                    r.ModifyDate = now; r.ModifierUserId = Me;
                    try { await _uow.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException) { return Bad<PcRevisionDto>(PcText.Concurrency); }
                    foreach (PcDocumentRevision old in previous) old.SupersededByRevisionId = r.Id;
                    await NoticesAsync(d, r, ct);
                    await _uow.SaveChangesAsync(ct);
                    return await RevisionResultAsync(r, ct);
                }
                case "as-built":
                {
                    if (r.Status != PcDocStatus.Released) return Bad<PcRevisionDto>("فقط بازنگری منتشرشده به As-Built تبدیل می‌شود.");
                    if (!p.CanRelease) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (r.PreparedByUserId == Me) return Bad<PcRevisionDto>(PcText.SameUser);
                    r.Status = PcDocStatus.AsBuilt; r.AsBuiltByUserId = Me; r.AsBuiltAt = now;
                    Log(d.Id, r.Id, PcDocEventKind.AsBuilt, note, now);
                    break;
                }
                case "obsolete":
                {
                    if (r.Status != PcDocStatus.Released) return Bad<PcRevisionDto>("فقط بازنگری منتشرشده منسوخ می‌شود.");
                    if (!p.CanRelease) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (r.PreparedByUserId == Me) return Bad<PcRevisionDto>(PcText.SameUser);
                    if (note == null || note.Length < 3) return Bad<PcRevisionDto>("علت منسوخ‌کردن الزامی است (حداقل ۳ حرف).");
                    r.Status = PcDocStatus.Obsolete; r.ObsoleteReason = note;
                    Log(d.Id, r.Id, PcDocEventKind.Obsoleted, note, now);
                    break;
                }
                case "link-change":
                {
                    if (!EdmsRules.IsEditable(r.Status)) return Bad<PcRevisionDto>("پیوند به درخواست تغییر فقط در پیش‌نویس تغییر می‌کند.");
                    if (!p.CanPrepare) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (d.CheckedOutByUserId.HasValue && d.CheckedOutByUserId != Me) return Bad<PcRevisionDto>(LockedBy);
                    if (dto.ChangeRequestId.HasValue)
                    {
                        string err = await ChangeRequestErrorAsync(dto.ChangeRequestId.Value, d.ContractId, ct);
                        if (err != null) return Bad<PcRevisionDto>(err);
                    }
                    r.ChangeRequestId = dto.ChangeRequestId;
                    r.ChangeImpact = dto.ChangeImpact ?? (dto.ChangeRequestId.HasValue || r.ChangeImpact);
                    break;
                }
                case "link-quality":
                {
                    if (!p.CanPrepare) return Bad<PcRevisionDto>(PcText.NotAllowed);
                    if (r.Status == PcDocStatus.Superseded) return Bad<PcRevisionDto>("بازنگری جایگزین‌شده پیوند تازه نمی‌گیرد.");
                    if (!dto.QualityKind.HasValue || !Enum.IsDefined(typeof(PcQualityLinkKind), dto.QualityKind.Value) || !dto.QualityTargetId.HasValue) return Bad<PcRevisionDto>("NCR یا سابقه کنترل کیفیت را مشخص کنید.");
                    bool exists = dto.QualityKind == PcQualityLinkKind.Ncr
                        ? await _uow.Set<NonConformance>().AnyAsync(n => n.Id == dto.QualityTargetId.Value && !n.Deleted, ct)
                        : await _uow.Set<QcRecord>().AnyAsync(n => n.Id == dto.QualityTargetId.Value && !n.Deleted, ct);
                    if (!exists) return PcText.NotFound<PcRevisionDto>("NCR یا سابقه کنترل کیفیت پیدا نشد.");
                    if (await _uow.Set<PcDocumentQualityLink>().AnyAsync(q => !q.Deleted && q.RevisionId == r.Id && q.Kind == dto.QualityKind.Value && q.TargetId == dto.QualityTargetId.Value, ct)) return Bad<PcRevisionDto>("این پیوند قبلاً ثبت شده است.");
                    _uow.Set<PcDocumentQualityLink>().Add(new PcDocumentQualityLink { RevisionId = r.Id, Kind = dto.QualityKind.Value, TargetId = dto.QualityTargetId.Value, Note = note, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me });
                    Log(d.Id, r.Id, PcDocEventKind.Linked, (dto.QualityKind == PcQualityLinkKind.Ncr ? "NCR " : "QC ") + dto.QualityTargetId, now);
                    await _uow.SaveChangesAsync(ct);
                    return await RevisionResultAsync(r, ct);
                }
                default:
                    return Bad<PcRevisionDto>("عملیات نامعتبر است.");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcRevisionDto>(PcText.Concurrency); }
            if (action == "reject") await NotifyPreparerAsync(r, d, ct);
            return await RevisionResultAsync(r, ct);
        }

        private async Task NotifyPreparerAsync(PcDocumentRevision r, PcEngineeringDeliverable d, CancellationToken ct)
        {
            if (_notify == null) return;
            try
            {
                await _notify.NotifyAsync(new NotifyRequest
                {
                    SourceKey = "edms-reject:" + r.Id, Module = "project-control", SourceType = "document-revision", SourceId = r.Id, Title = $"سند {d.DocumentNumber} ویرایش {r.Code} رد شد: {r.RejectReason}",
                    Link = "/project-control/edms?deliverable=" + d.Id, UserIds = new List<int> { r.PreparedByUserId }, Kind = OfInboxKind.Task,
                }, ct);
            }
            catch (Exception) { /* the inbox never blocks a document step */ }
        }

        /// <summary>Notices to the distribution list of the discipline when a revision is released (in-app inbox; SMS / e-mail stay disabled).</summary>
        private async Task NoticesAsync(PcEngineeringDeliverable d, PcDocumentRevision r, CancellationToken ct)
        {
            string disc = (Clean(d.Discipline) ?? "").ToUpperInvariant();
            List<PcDocumentDistribution> rows = (await _uow.Set<PcDocumentDistribution>().AsNoTracking().Where(x => !x.Deleted && x.IsActive && (x.ContractId == null || x.ContractId == d.ContractId)).ToListAsync(ct))
                .Where(x => string.Equals((Clean(x.Discipline) ?? "").ToUpperInvariant(), disc, StringComparison.Ordinal)).ToList();
            List<int> users = rows.Select(x => x.UserId).Distinct().ToList();
            if (users.Count == 0) return;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (int u in users)
                _uow.Set<PcDocumentNotice>().Add(new PcDocumentNotice { RevisionId = r.Id, UserId = u, NotifiedAt = now });
            if (_notify == null) return;
            foreach (int u in users)
            {
                try
                {
                    await _notify.NotifyAsync(new NotifyRequest
                    {
                        SourceKey = "edms-release:" + r.Id + ":" + u, Module = "project-control", SourceType = "document-notice", SourceId = r.Id, Title = $"انتشار سند {d.DocumentNumber} ویرایش {r.Code} - {d.Title}",
                        Link = "/project-control/edms?deliverable=" + d.Id, UserIds = new List<int> { u }, Kind = OfInboxKind.Task,
                    }, ct);
                }
                catch (Exception) { /* the inbox never blocks a release */ }
            }
        }

        // ============================================================================ compare

        public async Task<ResponseDto<PcRevisionCompareDto>> CompareAsync(int revisionA, int revisionB, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.Readers)) return Bad<PcRevisionCompareDto>(PcText.NotAllowed);
            if (revisionA == revisionB) return Bad<PcRevisionCompareDto>("دو بازنگری متفاوت را انتخاب کنید.");
            List<PcDocumentRevision> revs = await _uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted && (r.Id == revisionA || r.Id == revisionB)).ToListAsync(ct);
            if (revs.Count != 2) return PcText.NotFound<PcRevisionCompareDto>("بازنگری پیدا نشد.");
            if (revs[0].DeliverableId != revs[1].DeliverableId) return Bad<PcRevisionCompareDto>("دو بازنگری باید از یک سند باشند.");
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().FirstAsync(x => x.Id == revs[0].DeliverableId, ct);
            List<PcRevisionDto> dtos = await BuildAsync(revs, d, ct);
            PcRevisionDto a = dtos.First(x => x.Id == revisionA), b = dtos.First(x => x.Id == revisionB);
            return ResponseDtoHelper.Ok(new PcRevisionCompareDto { A = a, B = b, Differences = EdmsRules.Diff(a, b) });
        }

        /// <summary>The revisions that cite a change request, an NCR or a QC record (the other direction of the link).</summary>
        public async Task<ResponseDto<List<PcRevisionDto>>> RevisionsOfAsync(string kind, int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EdmsRoles.Readers)) return Bad<List<PcRevisionDto>>(PcText.NotAllowed);
            IQueryable<PcDocumentRevision> q = _uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted);
            switch ((kind ?? "").Trim().ToLowerInvariant())
            {
                case "change": q = q.Where(r => r.ChangeRequestId == id); break;
                case "ncr":
                case "qc":
                {
                    PcQualityLinkKind qk = kind.Trim().ToLowerInvariant() == "ncr" ? PcQualityLinkKind.Ncr : PcQualityLinkKind.QcRecord;
                    List<int> ids = await _uow.Set<PcDocumentQualityLink>().AsNoTracking().Where(l => !l.Deleted && l.Kind == qk && l.TargetId == id).Select(l => l.RevisionId).ToListAsync(ct);
                    q = q.Where(r => ids.Contains(r.Id));
                    break;
                }
                default: return Bad<List<PcRevisionDto>>("نوع مرجع نامعتبر است (change، ncr یا qc).");
            }
            List<PcDocumentRevision> revs = await q.OrderBy(r => r.DeliverableId).ThenBy(r => r.Sequence).Take(200).ToListAsync(ct);
            var result = new List<PcRevisionDto>();
            foreach (IGrouping<int, PcDocumentRevision> g in revs.GroupBy(r => r.DeliverableId))
            {
                PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().FirstAsync(x => x.Id == g.Key, ct);
                result.AddRange(await BuildAsync(g.ToList(), d, ct));
            }
            return ResponseDtoHelper.Ok(result);
        }

        // ============================================================================ check-out

        private async Task<(PcEngineeringDeliverable Doc, ResponseDto<PcDeliverableHistoryDto> Error)> LoadForLockAsync(PcCheckOutDto dto, CancellationToken ct)
        {
            if (dto == null) return (null, Bad<PcDeliverableHistoryDto>(PcText.InvalidData));
            PcEngineeringDeliverable d = await _uow.Set<PcEngineeringDeliverable>().FirstOrDefaultAsync(x => x.Id == dto.DeliverableId && !x.Deleted, ct);
            if (d == null) return (null, PcText.NotFound<PcDeliverableHistoryDto>("سند پیدا نشد."));
            return (d, null);
        }

        public async Task<ResponseDto<PcDeliverableHistoryDto>> CheckOutAsync(PcCheckOutDto dto, CancellationToken ct)
        {
            var (d, error) = await LoadForLockAsync(dto, ct);
            if (error != null) return error;
            Perms p = await PermsAsync(d.Discipline, d.ContractId, ct);
            if (!p.CanPrepare) return Bad<PcDeliverableHistoryDto>(PcText.NotAllowed);
            if (d.ReceivedFromClient) return Bad<PcDeliverableHistoryDto>("سند دریافت‌شده از کارفرما ویرایش نمی‌شود.");
            if (d.CheckedOutByUserId.HasValue)
            {
                string who = NameOf(await NamesAsync(new int?[] { d.CheckedOutByUserId }, ct), d.CheckedOutByUserId);
                return Bad<PcDeliverableHistoryDto>(d.CheckedOutByUserId == Me ? "این سند را خودتان برداشته‌اید." : $"سند را {who} از {FiscalText(d.CheckedOutAt)} برداشته است.");
            }
            DateTimeOffset now = DateTimeOffset.Now;
            d.CheckedOutByUserId = Me; d.CheckedOutAt = now; d.CheckOutNote = Clean(dto.Note); d.ModifyDate = now; d.ModifierUserId = Me;
            Log(d.Id, null, PcDocEventKind.CheckedOut, dto.Note, now);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcDeliverableHistoryDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await HistoryAsync(d, ct));
        }

        private static string FiscalText(DateTimeOffset? at) => at.HasValue ? global::Services.NasbNiro.Finance.FiscalCalendars.Current.TextTime(at.Value.LocalDateTime) : "";

        public async Task<ResponseDto<PcDeliverableHistoryDto>> CheckInAsync(PcCheckOutDto dto, CancellationToken ct)
        {
            var (d, error) = await LoadForLockAsync(dto, ct);
            if (error != null) return error;
            if (!d.CheckedOutByUserId.HasValue) return Bad<PcDeliverableHistoryDto>("سند برداشته نشده است.");
            if (d.CheckedOutByUserId != Me) return Bad<PcDeliverableHistoryDto>("فقط کسی که سند را برداشته آن را بازمی‌گرداند؛ مدیر مدارک می‌تواند برداشت را با ذکر علت لغو کند.");
            DateTimeOffset now = DateTimeOffset.Now;
            d.CheckedOutByUserId = null; d.CheckedOutAt = null; d.CheckOutNote = null; d.ModifyDate = now; d.ModifierUserId = Me;
            Log(d.Id, null, PcDocEventKind.CheckedIn, dto.Note, now);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcDeliverableHistoryDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await HistoryAsync(d, ct));
        }

        public async Task<ResponseDto<PcDeliverableHistoryDto>> CancelCheckOutAsync(PcCheckOutDto dto, CancellationToken ct)
        {
            var (d, error) = await LoadForLockAsync(dto, ct);
            if (error != null) return error;
            if (!_user.IsInRole(EdmsRoles.Managers)) return Bad<PcDeliverableHistoryDto>(PcText.NotAllowed + " (لغو برداشت با مدیر مدارک است)");
            if (!d.CheckedOutByUserId.HasValue) return Bad<PcDeliverableHistoryDto>("سند برداشته نشده است.");
            string reason = Clean(dto.Reason);
            if (reason == null || reason.Length < 3) return Bad<PcDeliverableHistoryDto>("علت لغو برداشت الزامی است (حداقل ۳ حرف).");
            DateTimeOffset now = DateTimeOffset.Now;
            int holder = d.CheckedOutByUserId.Value;
            d.CheckedOutByUserId = null; d.CheckedOutAt = null; d.CheckOutNote = null; d.ModifyDate = now; d.ModifierUserId = Me;
            Log(d.Id, null, PcDocEventKind.CheckoutCancelled, $"لغو برداشت کاربر {holder}: {reason}", now);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<PcDeliverableHistoryDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(await HistoryAsync(d, ct));
        }
    }
}
