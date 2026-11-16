using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    internal static class EnReferenceRules
    {
        public static string Key(string documentType) => (documentType ?? string.Empty).Trim().ToUpperInvariant();

        /// <summary>The canonical content an approval binds to: the lines in order, with everything that decides what "complete" means.</summary>
        public static string Hash(string code, int revision, IEnumerable<EnReferenceLine> lines) =>
            EnHash.Of(EnHash.Norm(code) + "#" + EnText.Num(revision) + "#" + string.Join(";", lines.OrderBy(l => l.Seq).Select(l =>
                string.Join("|", EnText.Num(l.Seq), EnHash.Norm(l.DocumentType), EnHash.Norm(l.Discipline), EnHash.Norm(l.Title), l.Required ? "1" : "0", l.TenderStage ? "1" : "0", l.ClientSubmittal ? "1" : "0", EnHash.Norm(l.StandardClause)))));

        /// <summary>The current list: the approved list with the latest approval.</summary>
        public static async Task<EnReferenceList> CurrentAsync(IUnitOfWork uow, bool track, CancellationToken ct, string code = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;   // the standard is set by the client, never assumed (not even when only one list is approved)
            IQueryable<EnReferenceList> q = uow.Set<EnReferenceList>();
            if (!track) q = q.AsNoTracking();
            return await q.Include(l => l.Lines).Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == code).OrderByDescending(l => l.ApprovedAt).ThenByDescending(l => l.Id).FirstOrDefaultAsync(ct);
        }

        /// <summary>The Codes (standard bases, e.g. API 611 and API 612) that have an approved list right now; one approved list per Code.</summary>
        public static Task<List<string>> ApprovedCodesAsync(IUnitOfWork uow, CancellationToken ct) =>
            uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved).Select(l => l.Code).Distinct().OrderBy(c => c).ToListAsync(ct);
    }

    public interface IEnReferenceService
    {
        Task<ResponseDto<PagedList<EnReferenceListDto>>> GetListsAsync(EnReferenceQuery query, CancellationToken ct);
        Task<ResponseDto<EnReferenceListDto>> GetListAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnReferenceListDto>> GetCurrentAsync(string code, CancellationToken ct);
        Task<ResponseDto<EnReferenceListDto>> SaveListAsync(EnSaveReferenceDto dto, CancellationToken ct);
        Task<ResponseDto<EnReferenceListDto>> ListActionAsync(EnReferenceActionDto dto, CancellationToken ct);
    }

    /// <summary>The reference list of engineering documents (what "complete" means for a platform). Drafted by an engineer or the agent, approved by the engineering manager or the CEO, never by someone who touched it.</summary>
    public class EnReferenceService : IEnReferenceService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public EnReferenceService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;

        // ---------------------------------------------------------------- the rules (one predicate for the button and the service)

        private static string EditReason(EnReferenceList l, bool writer) =>
            !writer ? EnText.NotAllowed : l.Status != EnRefStatus.Draft ? "فقط فهرست پیش‌نویس ویرایش می‌شود؛ برای تغییر فهرست تاییدشده بازنگری تازه بسازید." : null;

        private static string SubmitReason(EnReferenceList l, bool writer, bool seat) =>
            !writer ? EnText.NotAllowed : seat ? EnText.AiNever : l.Status != EnRefStatus.Draft ? "فقط فهرست پیش‌نویس برای تایید ارسال می‌شود." : l.Lines.Count == 0 ? "فهرست بدون ردیف را نمی‌توان ارسال کرد." : null;

        private static string ApproveReason(EnReferenceList l, bool approver, bool seat, int me) =>
            !approver ? EnText.NotAllowed : seat ? EnText.AiNever : l.Status != EnRefStatus.UnderReview ? "فقط فهرست در انتظار تایید، تایید می‌شود."
            : EnEditors.Touched(l.PreparedByUserId, l.EditorUserIds, me) || l.CreatorUserId == me ? EnText.SameUser
            : l.ContentHash != EnReferenceRules.Hash(l.Code, l.Revision, l.Lines) ? "محتوای فهرست پس از ارسال تغییر کرده است؛ آن را به پیش‌نویس برگردانید و دوباره ارسال کنید." : null;

        private static string ReturnReasonOf(EnReferenceList l, bool approver) =>
            !approver ? EnText.NotAllowed : l.Status != EnRefStatus.UnderReview ? "فقط فهرست در انتظار تایید برگشت می‌خورد." : null;

        private static string ReviseReason(EnReferenceList l, bool writer, bool hasOpenRevision) =>
            !writer ? EnText.NotAllowed : l.Status != EnRefStatus.Approved ? "بازنگری فقط از فهرست تاییدشدهٔ جاری ساخته می‌شود." : hasOpenRevision ? "برای این فهرست بازنگری باز (پیش‌نویس یا در انتظار تایید) وجود دارد." : null;

        private static string VoidReasonOf(EnReferenceList l, bool approver) =>
            !approver ? EnText.NotAllowed : l.Status != EnRefStatus.Draft && l.Status != EnRefStatus.UnderReview ? "فقط فهرست پیش‌نویس یا در انتظار تایید ابطال می‌شود." : null;

        private const string LooseningText = "این بازنگری بند الزامی را حذف یا سبک کرده است؛ تایید آن با مدیر مهندسی و سپس مدیرعامل (دو نفر متفاوت) از مسیر انحراف از استاندارد است.";

        // ---------------------------------------------------------------- read

        private async Task<List<EnReferenceListDto>> DtosAsync(List<EnReferenceList> rows, bool withLines, CancellationToken ct)
        {
            bool writer = _user.IsInRole(EnRoles.Writers), approver = _user.IsInRole(EnRoles.RefApprovers);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.SelectMany(r => new int?[] { r.PreparedByUserId, r.ApprovedByUserId }), ct);
            List<string> codes = rows.Select(r => r.Code).Distinct().ToList();
            List<string> openCodes = await _uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && codes.Contains(l.Code) && (l.Status == EnRefStatus.Draft || l.Status == EnRefStatus.UnderReview)).Select(l => l.Code).Distinct().ToListAsync(ct);
            List<int> ids = rows.Select(r => r.Id).ToList();
            List<int> viaDeviation = await _uow.Set<EnDeviation>().AsNoTracking().Where(x => !x.Deleted && x.TargetType == EnDeviations.ListType && ids.Contains(x.TargetId) && (x.Status == EnDeviationStatus.Requested || x.Status == EnDeviationStatus.ManagerApproved)).Select(x => x.TargetId).ToListAsync(ct);
            return rows.Select(l =>
            {
                string edit = EditReason(l, writer), submit = SubmitReason(l, writer, seat), approve = viaDeviation.Contains(l.Id) ? LooseningText : ApproveReason(l, approver, seat, Me), ret = ReturnReasonOf(l, approver), revise = ReviseReason(l, writer, openCodes.Contains(l.Code)), voidR = VoidReasonOf(l, approver);
                return new EnReferenceListDto
                {
                    Id = l.Id, Code = l.Code, Title = l.Title, StandardSource = l.StandardSource, Revision = l.Revision, Status = (int)l.Status, StatusText = EnText.RefStatus(l.Status), IsCurrent = l.Status == EnRefStatus.Approved,   // one approved list per Code
                    PreparedByUserId = l.PreparedByUserId, PreparedByName = names.Name(l.PreparedByUserId), SubmittedAt = l.SubmittedAt, SubmittedAtText = EnText.Stamp(l.SubmittedAt),
                    ApprovedByUserId = l.ApprovedByUserId, ApprovedByName = names.Name(l.ApprovedByUserId), ApprovedAt = l.ApprovedAt, ApprovedAtText = EnText.Stamp(l.ApprovedAt), Note = l.Note, VoidReason = l.VoidReason,
                    LineCount = l.Lines.Count, RequiredCount = l.Lines.Count(x => x.Required), TenderStageCount = l.Lines.Count(x => x.TenderStage), TouchedByMe = EnEditors.Touched(l.PreparedByUserId, l.EditorUserIds, Me),
                    Lines = withLines ? l.Lines.OrderBy(x => x.Seq).Select(x => new EnReferenceLineDto { Id = x.Id, Seq = x.Seq, DocumentType = x.DocumentType, Discipline = x.Discipline, Title = x.Title, Required = x.Required, TenderStage = x.TenderStage, ClientSubmittal = x.ClientSubmittal, StandardClause = x.StandardClause, Note = x.Note }).ToList() : new List<EnReferenceLineDto>(),
                    RowVersion = EnConcurrency.Text(l.RowVersion),
                    CanEdit = edit == null, EditBlockedReason = edit, CanSubmit = submit == null, SubmitBlockedReason = submit, CanApprove = approve == null, ApproveBlockedReason = approve,
                    CanReturn = ret == null, ReturnBlockedReason = ret, CanRevise = revise == null, ReviseBlockedReason = revise, CanVoid = voidR == null, VoidBlockedReason = voidR,
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<EnReferenceListDto>>> GetListsAsync(EnReferenceQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnReferenceListDto>>(EnText.NotAllowed);
            query ??= new EnReferenceQuery();
            IQueryable<EnReferenceList> q = _uow.Set<EnReferenceList>().AsNoTracking().Include(l => l.Lines).Where(l => !l.Deleted);
            if (query.Status.HasValue) { EnRefStatus s = (EnRefStatus)query.Status.Value; q = q.Where(l => l.Status == s); }
            string text = EnText.Clean(query.Contains);
            if (text != null) q = q.Where(l => l.Code.Contains(text) || l.Title.Contains(text));
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(l => l.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, false, ct), ct));
        }

        public async Task<ResponseDto<EnReferenceListDto>> GetListAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnReferenceListDto>(EnText.NotAllowed);
            EnReferenceList l = await _uow.Set<EnReferenceList>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (l == null) return EnText.NotFound<EnReferenceListDto>(EnText.NotFoundList);
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnReferenceList> { l }, true, ct))[0]);
        }

        public async Task<ResponseDto<EnReferenceListDto>> GetCurrentAsync(string code, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnReferenceListDto>(EnText.NotAllowed);
            if (string.IsNullOrWhiteSpace(code))
            {
                // a screen helper only (viewing): the one approved list when there is exactly one; the services never assume it
                List<string> codes = await EnReferenceRules.ApprovedCodesAsync(_uow, ct);
                if (codes.Count == 1) code = codes[0];
            }
            EnReferenceList l = await EnReferenceRules.CurrentAsync(_uow, false, ct, code);
            if (l == null) return EnText.NotFound<EnReferenceListDto>("فهرست مدارک مرجع تاییدشده‌ای وجود ندارد؛ ابتدا فهرست را تهیه و تایید کنید.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnReferenceList> { l }, true, ct))[0]);
        }

        // ---------------------------------------------------------------- save

        private static string Validate(EnSaveReferenceDto d)
        {
            string code = EnText.Clean(d.Code), title = EnText.Clean(d.Title);
            if (code == null || title == null) return "کد و عنوان فهرست الزامی است.";
            string p = EnText.Over(code, 60, "کد") ?? EnText.Over(title, 300, "عنوان") ?? EnText.Over(d.StandardSource, 200, "منبع استاندارد") ?? EnText.Over(d.Note, 1000, "توضیح");
            if (p != null) return p;
            if (d.Lines == null || d.Lines.Count == 0) return "فهرست دست‌کم یک ردیف می‌خواهد.";
            if (d.Lines.Count > 300) return "فهرست بیش از ۳۰۰ ردیف نمی‌تواند باشد.";
            foreach (EnReferenceLineDto x in d.Lines)
            {
                if (EnText.Clean(x.DocumentType) == null || EnText.Clean(x.Title) == null) return "نوع و عنوان هر ردیف الزامی است.";
                p = EnText.Over(x.DocumentType, 100, "نوع مدرک") ?? EnText.Over(x.Discipline, 100, "رشته") ?? EnText.Over(x.Title, 300, "عنوان ردیف") ?? EnText.Over(x.StandardClause, 300, "بند استاندارد") ?? EnText.Over(x.Note, 500, "توضیح ردیف");
                if (p != null) return p;
            }
            return null;
        }

        private static void ApplyLines(EnReferenceList l, List<EnReferenceLineDto> lines)
        {
            l.Lines.Clear();
            int seq = 0;
            foreach (EnReferenceLineDto x in lines)
                l.Lines.Add(new EnReferenceLine { Seq = ++seq, DocumentType = x.DocumentType.Trim(), Discipline = EnText.Clean(x.Discipline), Title = x.Title.Trim(), Required = x.Required, TenderStage = x.TenderStage, ClientSubmittal = x.ClientSubmittal, StandardClause = EnText.Clean(x.StandardClause), Note = EnText.Clean(x.Note) });
        }

        public async Task<ResponseDto<EnReferenceListDto>> SaveListAsync(EnSaveReferenceDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Writers)) return EnText.Bad<EnReferenceListDto>(EnText.NotAllowed);
            if (dto == null) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
            string problem = Validate(dto);
            if (problem != null) return EnText.Bad<EnReferenceListDto>(problem);
            DateTimeOffset now = DateTimeOffset.Now;
            EnReferenceList row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<EnReferenceList>().Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == dto.Id.Value && !l.Deleted, ct);
                if (row == null) return EnText.NotFound<EnReferenceListDto>(EnText.NotFoundList);
                string why = EditReason(row, true);
                if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                if (!EnConcurrency.Apply(_uow, row, dto.RowVersion)) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
                if (!string.Equals(row.Code, dto.Code.Trim(), StringComparison.Ordinal)) return EnText.Bad<EnReferenceListDto>("کد فهرست پس از ساخت تغییر نمی‌کند.");
                foreach (EnReferenceLine old in row.Lines.ToList()) _uow.Set<EnReferenceLine>().Remove(old);
                row.Lines.Clear();
            }
            else
            {
                string code = dto.Code.Trim();
                if (await _uow.Set<EnReferenceList>().AnyAsync(l => !l.Deleted && l.Code == code && (l.Status == EnRefStatus.Draft || l.Status == EnRefStatus.UnderReview), ct))
                    return EnText.Bad<EnReferenceListDto>("برای این کد، فهرست باز (پیش‌نویس یا در انتظار تایید) وجود دارد؛ همان را ویرایش کنید.");
                int revision = (await _uow.Set<EnReferenceList>().Where(l => l.Code == code).Select(l => (int?)l.Revision).MaxAsync(ct) ?? 0) + 1;
                row = new EnReferenceList { Code = code, Revision = revision, Status = EnRefStatus.Draft, PreparedByUserId = Me, CreateDate = now, CreatorUserId = Me };
                _uow.Set<EnReferenceList>().Add(row);
            }
            row.Title = dto.Title.Trim(); row.StandardSource = EnText.Clean(dto.StandardSource); row.Note = EnText.Clean(dto.Note);
            ApplyLines(row, dto.Lines);
            row.EditorUserIds = EnEditors.With(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnReferenceListDto>(EnText.Concurrency); }
            return await GetListAsync(row.Id, ct);
        }

        // ---------------------------------------------------------------- actions

        public async Task<ResponseDto<EnReferenceListDto>> ListActionAsync(EnReferenceActionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "submit" && action != "approve" && action != "return" && action != "revise" && action != "void") return EnText.Bad<EnReferenceListDto>("عمل نامعتبر است (submit، approve، return، revise یا void).");
            EnReferenceList row = await _uow.Set<EnReferenceList>().Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == dto.Id && !l.Deleted, ct);
            if (row == null) return EnText.NotFound<EnReferenceListDto>(EnText.NotFoundList);
            if (!EnConcurrency.Apply(_uow, row, dto.RowVersion)) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
            bool writer = _user.IsInRole(EnRoles.Writers), approver = _user.IsInRole(EnRoles.RefApprovers);
            bool seat = await EnActors.IsSeatAsync(_uow, Me, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string note = EnText.Clean(dto.Note);
            int resultId = row.Id;
            string why;
            switch (action)
            {
                case "submit":
                    why = SubmitReason(row, writer, seat);
                    if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                    row.Status = EnRefStatus.UnderReview; row.SubmittedAt = now; row.EditorUserIds = EnEditors.With(row.EditorUserIds, Me);   // the submitter is a maker too
                    row.Status = EnRefStatus.UnderReview; row.SubmittedAt = now; row.ContentHash = EnReferenceRules.Hash(row.Code, row.Revision, row.Lines);
                    {
                        // a revision that removes a line or un-marks Required compared with the previous APPROVED revision of the code needs the head of engineering AND the CEO
                        EnReferenceList previous = await EnReferenceRules.CurrentAsync(_uow, false, ct, row.Code);
                        List<string> loosened = previous == null || previous.Id == row.Id ? new List<string>() : EnDeviations.Loosened(previous, row);
                        if (loosened.Count > 0)
                        {
                            EnDeviation dev = new EnDeviation
                            {
                                Kind = EnDeviationKind.ReferenceLoosening, TargetType = EnDeviations.ListType, TargetId = row.Id, StandardCode = row.Code, Requirement = EnText.Cut(string.Join("، ", loosened), 300),
                                Instruction = EnText.Cut("بازنگری " + EnText.Num(row.Revision) + " فهرست مرجع " + row.Code + " نسبت به بازنگری تاییدشدهٔ " + EnText.Num(previous.Revision) + " این بندها را سبک می‌کند: " + string.Join("، ", loosened) + (EnText.Clean(row.Note) == null ? string.Empty : ". " + row.Note), 2000),
                                RequestedByUserId = Me, RequestedAt = now, Status = EnDeviationStatus.Requested, ContentHash = EnDeviations.ListHash(row), CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                            };
                            dev.Number = await EnNumbers.NextAsync(_uow, EnNumbers.Deviation, "EDV", now.DateTime, ct);
                            _uow.Set<EnDeviation>().Add(dev);
                            row.ModifyDate = now; row.ModifierUserId = Me;
                            try { await _uow.SaveChangesAsync(ct); }
                            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnReferenceListDto>(EnText.Concurrency); }
                            await EnDeviations.SyncAsync(_notify, dev, ct);
                            return await GetListAsync(row.Id, ct);
                        }
                    }
                    break;
                case "approve":
                    why = ApproveReason(row, approver, seat, Me);
                    if (why == null && await _uow.Set<EnDeviation>().AnyAsync(x => !x.Deleted && x.TargetType == EnDeviations.ListType && x.TargetId == row.Id && (x.Status == EnDeviationStatus.Requested || x.Status == EnDeviationStatus.ManagerApproved), ct)) why = LooseningText;
                    if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                    // one approved list per code: the earlier revision is superseded first (a filtered unique index keeps it single)
                    List<EnReferenceList> older = await _uow.Set<EnReferenceList>().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == row.Code && l.Id != row.Id).ToListAsync(ct);
                    if (older.Count > 0)
                    {
                        foreach (EnReferenceList o in older) { o.Status = EnRefStatus.Superseded; o.ModifyDate = now; o.ModifierUserId = Me; }
                        try { await _uow.SaveChangesAsync(ct); }
                        catch (DbUpdateConcurrencyException) { return EnText.Bad<EnReferenceListDto>(EnText.Concurrency); }
                    }
                    row.Status = EnRefStatus.Approved; row.ApprovedAt = now; row.ApprovedByUserId = Me;
                    break;
                case "return":
                    why = ReturnReasonOf(row, approver);
                    if (why == null) why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل برگشت");
                    if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                    row.Status = EnRefStatus.Draft; row.ContentHash = null; row.SubmittedAt = null; row.Note = EnText.Cut("برگشت: " + note, 1000);
                    await EnDeviations.VoidOpenAsync(_uow, _notify, EnDeviations.ListType, row.Id, "فهرست برگشت خورد؛ درخواست انحراف باطل شد.", Me, ct);
                    break;
                case "void":
                    why = VoidReasonOf(row, approver);
                    if (why == null) why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل ابطال");
                    if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                    row.Status = EnRefStatus.Void; row.VoidReason = EnText.Cut(note, 1000);
                    await EnDeviations.VoidOpenAsync(_uow, _notify, EnDeviations.ListType, row.Id, "فهرست باطل شد؛ درخواست انحراف باطل شد.", Me, ct);
                    break;
                default:   // revise
                    bool open = await _uow.Set<EnReferenceList>().AnyAsync(l => !l.Deleted && l.Code == row.Code && (l.Status == EnRefStatus.Draft || l.Status == EnRefStatus.UnderReview), ct);
                    why = ReviseReason(row, writer, open);
                    if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                    int revision = (await _uow.Set<EnReferenceList>().Where(l => l.Code == row.Code).Select(l => (int?)l.Revision).MaxAsync(ct) ?? 0) + 1;
                    EnReferenceList copy = new EnReferenceList { Code = row.Code, Title = row.Title, StandardSource = row.StandardSource, Revision = revision, Status = EnRefStatus.Draft, PreparedByUserId = Me, EditorUserIds = EnEditors.With(null, Me), Note = note, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
                    foreach (EnReferenceLine x in row.Lines.OrderBy(x => x.Seq))
                        copy.Lines.Add(new EnReferenceLine { Seq = x.Seq, DocumentType = x.DocumentType, Discipline = x.Discipline, Title = x.Title, Required = x.Required, TenderStage = x.TenderStage, ClientSubmittal = x.ClientSubmittal, StandardClause = x.StandardClause, Note = x.Note });
                    _uow.Set<EnReferenceList>().Add(copy);
                    try { await _uow.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException) { return EnText.Bad<EnReferenceListDto>(EnText.Concurrency); }
                    return await GetListAsync(copy.Id, ct);
            }
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnReferenceListDto>(EnText.Concurrency); }
            return await GetListAsync(resultId, ct);
        }
    }
}
