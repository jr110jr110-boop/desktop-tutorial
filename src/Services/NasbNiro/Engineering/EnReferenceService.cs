using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services.Contracts;
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
                string.Join("|", EnText.Num(l.Seq), EnHash.Norm(l.DocumentType), EnHash.Norm(l.Discipline), EnHash.Norm(l.Title), l.Required ? "1" : "0", l.TenderStage ? "1" : "0", l.ClientSubmittal ? "1" : "0", EnHash.Norm(l.StandardClause)) + (string.IsNullOrEmpty(l.Units) ? string.Empty : "|U:" + l.Units))));

        /// <summary>The current list: the approved list with the latest approval.</summary>
        public static async Task<EnReferenceList> CurrentAsync(IUnitOfWork uow, bool track, CancellationToken ct, string code = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;   // the standard is set by the client, never assumed (not even when only one list is approved)
            IQueryable<EnReferenceList> q = uow.Set<EnReferenceList>();
            if (!track) q = q.AsNoTracking();
            return await q.Include(l => l.Lines).Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == code).OrderByDescending(l => l.ApprovedAt).ThenByDescending(l => l.Id).FirstOrDefaultAsync(ct);
        }

        /// <summary>The one check of "the standard is chosen, not typed" (platform native standard, registration): the code must be the Code of a list that is Approved right now; null when it is.</summary>
        public static async Task<string> ApprovedCodeProblemAsync(IUnitOfWork uow, string code, CancellationToken ct) =>
            !string.IsNullOrEmpty(code) && await uow.Set<EnReferenceList>().AsNoTracking().AnyAsync(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == code, ct) ? null
            : "استاندارد «" + code + "» فهرست MDL تاییدشده ندارد؛ استاندارد را از فهرست استانداردهای تاییدشده انتخاب کنید (در صورت نبود، ابتدا فهرست آن ساخته و تایید شود).";

        /// <summary>The refusal text of the CEO's approval of a case: the MDL of its standard is not approved (shared by the action and the DTO flag, so they never disagree).</summary>
        public static string CeoApprovalBlockedText(string code) =>
            "تایید مدیرعامل بسته است: فهرست مدارک مرجع (MDL) استاندارد «" + code + "» هنوز تایید نشده است. ابتدا مدیر مهندسی فهرست MDL این استاندارد را تایید کند.";

        /// <summary>The standing notice of the case page while the MDL of its standard is not approved.</summary>
        public static string StandardNoticeText(string code) =>
            "برای استاندارد «" + code + "» فهرست MDL تاییدشده‌ای نیست؛ تا تایید آن، تایید مدیرعامل روی این پرونده بسته است.";

        /// <summary>The one normalisation of a standard code (trim + upper-case invariant, the same as a case code); null for an empty code.</summary>
        public static string NormCode(string code) => EnDossierBuilder.NormCode(code);

        /// <summary>The one check "an approved MDL exists for this code": both sides normalised (trim + upper-case), so SQL Server (case-insensitive) and in-memory checks agree. Used by the approve action, the case flags, the notice and the agent gate text.</summary>
        public static async Task<bool> HasApprovedAsync(IUnitOfWork uow, string code, CancellationToken ct)
        {
            string norm = NormCode(code);
            return norm != null && await uow.Set<EnReferenceList>().AsNoTracking().AnyAsync(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code.Trim().ToUpper() == norm, ct);
        }

        /// <summary>The approved codes of one query as a set that matches with <see cref="IsApproved"/> (normalised, ordinal on the normal form).</summary>
        public static async Task<HashSet<string>> ApprovedSetAsync(IUnitOfWork uow, CancellationToken ct) =>
            (await ApprovedCodesAsync(uow, ct)).Select(NormCode).Where(c => c != null).ToHashSet(StringComparer.Ordinal);

        /// <summary>Set lookup with the same normalisation as <see cref="HasApprovedAsync"/>.</summary>
        public static bool IsApproved(HashSet<string> approvedSet, string code)
        {
            string norm = NormCode(code);
            return norm != null && approvedSet.Contains(norm);
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
        Task<ResponseDto<List<EnReferenceCodeDto>>> GetApprovedCodesAsync(CancellationToken ct);
        Task<ResponseDto<EnReferenceListDto>> SaveListAsync(EnSaveReferenceDto dto, CancellationToken ct);
        Task<ResponseDto<EnReferenceListDto>> ListActionAsync(EnReferenceActionDto dto, CancellationToken ct);
        /// <summary>The uploaded bytes are kept as evidence (SourceDocumentId, SourceFileName of the list) through the document store; <paramref name="fileName"/> is the original file name.</summary>
        Task<ResponseDto<EnReferenceListDto>> UploadLinesAsync(int id, string rowVersion, byte[] file, CancellationToken ct, string fileName = null);
        Task<ResponseDto<EnReferenceListDto>> CreateFromFileAsync(EnSaveReferenceDto header, byte[] file, CancellationToken ct, string fileName = null);
    }

    /// <summary>The reference list of engineering documents (what "complete" means for a platform). Drafted by an engineer or the agent, approved by the engineering manager or the CEO, never by someone who touched it.</summary>
    public class EnReferenceService : IEnReferenceService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;
        private readonly IDocumentService _documents;

        public EnReferenceService(IUnitOfWork uow, IInventoryUserContext user, global::Services.NasbNiro.Office.INotificationService notify = null, IDocumentService documents = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
            _documents = documents;
        }

        /// <summary>
        /// Keeps the uploaded CSV as evidence in the SAME document store as every other upload (the Documents endpoint's service); returns the id and the cut file name.
        /// Called only after every refusal of the upload is behind us, so a refused file leaves no orphan document. The store saves the unit of work: nothing of the list may be pending half-done when this runs.
        /// </summary>
        private async Task<(Guid? Id, string Name)> StoreSourceAsync(byte[] file, string fileName, string code)
        {
            if (_documents == null || file == null || file.Length == 0) return (null, null);
            string name = EnText.Cut(EnText.Clean(System.IO.Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'))) ?? "reference-list.csv", 260);
            IFormFile form = new FormFile(new System.IO.MemoryStream(file), 0, file.Length, "file", name);
            Guid id = await _documents.UploadDocumentAsync(new ViewModels.NasbNiro.DocumentDtos.UploadFileDto { File = form, Name = name, Description = EnText.Cut("فایل منبع فهرست مدارک مرجع (MDL) " + code, 300), Code = "engineering" });
            return (id, name);
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
                    ApprovedByUserId = l.ApprovedByUserId, ApprovedByName = names.Name(l.ApprovedByUserId), ApprovedAt = l.ApprovedAt, ApprovedAtText = EnText.Stamp(l.ApprovedAt), Note = l.Note, VoidReason = l.VoidReason, SourceDocumentId = l.SourceDocumentId, SourceFileName = l.SourceFileName,
                    LineCount = l.Lines.Count, RequiredCount = l.Lines.Count(x => x.Required), TenderStageCount = l.Lines.Count(x => x.TenderStage), TouchedByMe = EnEditors.Touched(l.PreparedByUserId, l.EditorUserIds, Me),
                    Lines = withLines ? l.Lines.OrderBy(x => x.Seq).Select(x => new EnReferenceLineDto { Id = x.Id, Seq = x.Seq, DocumentType = x.DocumentType, Discipline = x.Discipline, Title = x.Title, Required = x.Required, TenderStage = x.TenderStage, ClientSubmittal = x.ClientSubmittal, StandardClause = x.StandardClause, Note = x.Note, Units = x.Units }).ToList() : new List<EnReferenceLineDto>(),
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

        /// <summary>The code and title of every standard that has an approved list (the choice of the standard in a dialog); the latest approval of a code gives the title.</summary>
        public async Task<ResponseDto<List<EnReferenceCodeDto>>> GetApprovedCodesAsync(CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<List<EnReferenceCodeDto>>(EnText.NotAllowed);
            var rows = await _uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved).Select(l => new { l.Code, l.Title, l.ApprovedAt, l.Id }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(rows.GroupBy(r => r.Code).Select(g => g.OrderByDescending(r => r.ApprovedAt).ThenByDescending(r => r.Id).First())
                .OrderBy(r => r.Code, StringComparer.Ordinal).Select(r => new EnReferenceCodeDto { Code = r.Code, Title = r.Title }).ToList());
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
            if (l == null) return EnText.NotFound<EnReferenceListDto>("فهرست MDL تاییدشده‌ای وجود ندارد؛ ابتدا فهرست را تهیه و تایید کنید.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnReferenceList> { l }, true, ct))[0]);
        }

        // ---------------------------------------------------------------- save

        private static string Validate(EnSaveReferenceDto d, bool headerOnly = false)
        {
            string code = EnText.Clean(d.Code), title = EnText.Clean(d.Title);
            if (code == null || title == null) return "کد و عنوان فهرست الزامی است.";
            string p = EnText.Over(code, 60, "کد") ?? EnText.Over(title, 300, "عنوان") ?? EnText.Over(d.StandardSource, 200, "منبع استاندارد") ?? EnText.Over(d.Note, 1000, "توضیح");
            if (p != null) return p;
            if (headerOnly) return null;
            if (d.Lines == null || d.Lines.Count == 0) return "فهرست دست‌کم یک ردیف می‌خواهد.";
            if (d.Lines.Count > 300) return "فهرست بیش از ۳۰۰ ردیف نمی‌تواند باشد.";
            foreach (EnReferenceLineDto x in d.Lines)
            {
                if (EnText.Clean(x.DocumentType) == null || EnText.Clean(x.Title) == null) return "نوع و عنوان هر ردیف الزامی است.";
                p = EnText.Over(x.DocumentType, 100, "نوع مدرک") ?? EnText.Over(x.Discipline, 100, "رشته") ?? EnText.Over(x.Title, 300, "عنوان ردیف") ?? EnText.Over(x.StandardClause, 300, "بند استاندارد") ?? EnText.Over(x.Note, 500, "توضیح ردیف");
                if (p != null) return p;
                EnUnits.Normalize(x.Units, out p);
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>
        /// Updates the lines in place (by Id, else by type and title) and appends the new ones. A removed line is deleted physically only while the list was never submitted (a draft that is not a controlled record yet);
        /// once submitted it is kept and flagged Voided, so the audit trail keeps it. The list must be loaded with its voided lines (IgnoreQueryFilters) so the numbering stays unique.
        /// </summary>
        private static void ApplyLines(IUnitOfWork uow, EnReferenceList l, List<EnReferenceLineDto> lines, EnReferenceCsv.Columns fileColumns = null)
        {
            bool neverSubmitted = l.Status == EnRefStatus.Draft && !l.SubmittedAt.HasValue;
            List<EnReferenceLine> pool = l.Lines.Where(x => !x.Voided).ToList();
            HashSet<EnReferenceLine> kept = new HashSet<EnReferenceLine>();
            int seq = 0;
            foreach (EnReferenceLineDto x in lines)
            {
                string key = EnDossierBuilder.TypeTitle(x.DocumentType.Trim(), x.Title.Trim());
                EnReferenceLine line = (x.Id > 0 ? pool.FirstOrDefault(p => p.Id == x.Id && !kept.Contains(p)) : null) ?? pool.FirstOrDefault(p => !kept.Contains(p) && EnDossierBuilder.TypeTitle(p.DocumentType, p.Title) == key);
                bool isNew = line == null;
                if (isNew) { line = new EnReferenceLine(); l.Lines.Add(line); }
                kept.Add(line);
                line.Seq = ++seq; line.DocumentType = x.DocumentType.Trim(); line.Discipline = EnText.Clean(x.Discipline); line.Title = x.Title.Trim(); line.Required = x.Required; line.TenderStage = x.TenderStage;
                // a file without the optional columns never wipes what the matched line already holds (a revision draft copies them from the approved list)
                if (isNew || fileColumns == null || fileColumns.ClientSubmittal) line.ClientSubmittal = x.ClientSubmittal;
                if (isNew || fileColumns == null || fileColumns.Clause) line.StandardClause = EnText.Clean(x.StandardClause);
                if (isNew || fileColumns == null || fileColumns.Note) line.Note = EnText.Clean(x.Note);
                line.Units = EnUnits.Normalize(x.Units, out _);
            }
            foreach (EnReferenceLine gone in pool.Where(p => !kept.Contains(p)))
            {
                if (neverSubmitted) { l.Lines.Remove(gone); uow.Set<EnReferenceLine>().Remove(gone); }
                else gone.Voided = true;
            }
        }

        public Task<ResponseDto<EnReferenceListDto>> SaveListAsync(EnSaveReferenceDto dto, CancellationToken ct) => SaveListCoreAsync(dto, null, null, ct);

        /// <summary>The save of a list; <paramref name="sourceFile"/> (a CSV upload) is stored and pointed at only after every refusal, and a save through the JSON API (no file) never clears the pointer.</summary>
        private async Task<ResponseDto<EnReferenceListDto>> SaveListCoreAsync(EnSaveReferenceDto dto, byte[] sourceFile, string sourceName, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Writers)) return EnText.Bad<EnReferenceListDto>(EnText.NotAllowed);
            if (dto == null) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
            // a save of an existing list that sends no lines is a header save (code, title, source, note): the lines stay as they are, so the page never re-sends lines it did not type
            bool headerOnly = dto.Id.HasValue && dto.Lines == null;
            string problem = Validate(dto, headerOnly);
            if (problem != null) return EnText.Bad<EnReferenceListDto>(problem);
            DateTimeOffset now = DateTimeOffset.Now;
            EnReferenceList row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<EnReferenceList>().IgnoreQueryFilters().Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == dto.Id.Value && !l.Deleted, ct);
                if (row == null) return EnText.NotFound<EnReferenceListDto>(EnText.NotFoundList);
                string why = EditReason(row, true);
                if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                if (!EnConcurrency.Apply(_uow, row, dto.RowVersion)) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
                why = EnEditors.Full(row.EditorUserIds, Me);
                if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                if (!string.Equals(EnReferenceRules.NormCode(row.Code), EnReferenceRules.NormCode(dto.Code), StringComparison.Ordinal)) return EnText.Bad<EnReferenceListDto>("کد فهرست پس از ساخت تغییر نمی‌کند.");
            }
            else
            {
                string code = EnReferenceRules.NormCode(dto.Code);
                if (await _uow.Set<EnReferenceList>().AnyAsync(l => !l.Deleted && l.Code == code && (l.Status == EnRefStatus.Draft || l.Status == EnRefStatus.UnderReview), ct))
                    return EnText.Bad<EnReferenceListDto>("برای این کد، فهرست باز (پیش‌نویس یا در انتظار تایید) وجود دارد؛ همان را ویرایش کنید.");
                int revision = (await _uow.Set<EnReferenceList>().Where(l => l.Code == code).Select(l => (int?)l.Revision).MaxAsync(ct) ?? 0) + 1;
                row = new EnReferenceList { Code = code, Revision = revision, Status = EnRefStatus.Draft, PreparedByUserId = Me, CreateDate = now, CreatorUserId = Me };
            }
            if (sourceFile != null)
            {
                (Guid? docId, string docName) = await StoreSourceAsync(sourceFile, sourceName, row.Code);   // the new row is not attached yet, so nothing half-done is saved with the file
                row.SourceDocumentId = docId; row.SourceFileName = docName;
            }
            if (row.Id == 0) _uow.Set<EnReferenceList>().Add(row);
            row.Title = dto.Title.Trim(); row.StandardSource = EnText.Clean(dto.StandardSource); row.Note = EnText.Clean(dto.Note);
            if (!headerOnly) ApplyLines(_uow, row, dto.Lines);
            row.EditorUserIds = EnEditors.With(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnReferenceListDto>(EnText.Concurrency); }
            return await GetListAsync(row.Id, ct);
        }

        /// <summary>
        /// Replaces the lines of a DRAFT list with the lines of a CSV file (project instruction 6: a human enters input data by file in a test release). Same permission and status rules as saving a draft; an AI seat is refused;
        /// nothing is changed when any row is refused.
        /// </summary>
        public async Task<ResponseDto<EnReferenceListDto>> UploadLinesAsync(int id, string rowVersion, byte[] file, CancellationToken ct, string fileName = null)
        {
            if (!_user.IsInRole(EnRoles.Writers)) return EnText.Bad<EnReferenceListDto>(EnText.NotAllowed);
            if (await EnActors.IsSeatAsync(_uow, Me, ct)) return EnText.Bad<EnReferenceListDto>(EnText.AiNever);
            EnReferenceList row = await _uow.Set<EnReferenceList>().IgnoreQueryFilters().Include(l => l.Lines).FirstOrDefaultAsync(l => l.Id == id && !l.Deleted, ct);
            if (row == null) return EnText.NotFound<EnReferenceListDto>(EnText.NotFoundList);
            string why = EditReason(row, true);
            if (why != null) return EnText.Bad<EnReferenceListDto>(why);
            if (!EnConcurrency.Apply(_uow, row, rowVersion)) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
            why = EnEditors.Full(row.EditorUserIds, Me);
            if (why != null) return EnText.Bad<EnReferenceListDto>(why);
            string problem = EnReferenceCsv.Parse(file, out List<EnReferenceLineDto> lines, out EnReferenceCsv.Columns columns);
            if (problem != null) return EnText.Bad<EnReferenceListDto>(problem);
            (Guid? docId, string docName) = await StoreSourceAsync(file, fileName, row.Code);   // the file is parsed: keep it as evidence (a later upload replaces the pointer, the old document stays)
            if (docId.HasValue) { row.SourceDocumentId = docId; row.SourceFileName = docName; }
            ApplyLines(_uow, row, lines, columns);
            DateTimeOffset now = DateTimeOffset.Now;
            row.EditorUserIds = EnEditors.With(row.EditorUserIds, Me);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnReferenceListDto>(EnText.Concurrency); }
            return await GetListAsync(row.Id, ct);
        }

        /// <summary>A NEW draft list whose lines come from a CSV file (the header fields are typed, the lines are never typed). Same rules as saving a new draft; an AI seat is refused.</summary>
        public async Task<ResponseDto<EnReferenceListDto>> CreateFromFileAsync(EnSaveReferenceDto header, byte[] file, CancellationToken ct, string fileName = null)
        {
            if (!_user.IsInRole(EnRoles.Writers)) return EnText.Bad<EnReferenceListDto>(EnText.NotAllowed);
            if (header == null) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
            if (await EnActors.IsSeatAsync(_uow, Me, ct)) return EnText.Bad<EnReferenceListDto>(EnText.AiNever);
            string problem = EnReferenceCsv.Parse(file, out List<EnReferenceLineDto> lines, out _);
            if (problem != null) return EnText.Bad<EnReferenceListDto>(problem);
            header.Id = null; header.RowVersion = null; header.Lines = lines;
            return await SaveListCoreAsync(header, file, fileName, ct);
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
            string why = EnText.Over(note, 1000, "توضیح") ?? ((action == "submit" || action == "revise") && !seat ? EnEditors.Full(row.EditorUserIds, Me) : null);
            if (why != null) return EnText.Bad<EnReferenceListDto>(why);
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
                                Instruction = EnText.Cut("بازنگری " + EnText.Num(row.Revision) + " فهرست MDL " + row.Code + " نسبت به بازنگری تاییدشدهٔ " + EnText.Num(previous.Revision) + " این بندها را سبک می‌کند: " + string.Join("، ", loosened) + (EnText.Clean(row.Note) == null ? string.Empty : ". " + row.Note), 2000),
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
                    // one approved list per code: the earlier revision is superseded first (a filtered unique index keeps it single); both saves are one transaction, so a failure never leaves the code without an Approved list
                {
                    int approveId = row.Id; string approveVersion = dto.RowVersion;
                    ResponseDto<EnReferenceListDto> approved = await EnTransaction.InAsync(_uow, async () =>
                    {
                        EnReferenceList target = await _uow.Set<EnReferenceList>().FirstAsync(l => l.Id == approveId, ct);
                        if (!EnConcurrency.Apply(_uow, target, approveVersion)) return EnText.Bad<EnReferenceListDto>(EnText.InvalidData);
                        List<EnReferenceList> older = await _uow.Set<EnReferenceList>().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == target.Code && l.Id != target.Id).ToListAsync(ct);
                        try
                        {
                            if (older.Count > 0)
                            {
                                foreach (EnReferenceList o in older) { o.Status = EnRefStatus.Superseded; o.ModifyDate = now; o.ModifierUserId = Me; }
                                await _uow.SaveChangesAsync(ct);
                            }
                            target.Status = EnRefStatus.Approved; target.ApprovedAt = now; target.ApprovedByUserId = Me; target.ModifyDate = now; target.ModifierUserId = Me;
                            await _uow.SaveChangesAsync(ct);
                        }
                        catch (DbUpdateConcurrencyException) { return EnText.Bad<EnReferenceListDto>(EnText.Concurrency); }
                        return ResponseDtoHelper.Ok<EnReferenceListDto>(null);
                    }, r => r.Success, ct);
                    if (!approved.Success) return approved;
                    return await GetListAsync(approveId, ct);
                }
                case "return":
                    why = ReturnReasonOf(row, approver);
                    if (why == null) why = EnText.ReasonShort(note, EnRules.ReasonMin, "دلیل برگشت");
                    if (why != null) return EnText.Bad<EnReferenceListDto>(why);
                    row.Status = EnRefStatus.Draft; row.ContentHash = null; row.Note = EnText.Cut("برگشت: " + note, 1000);
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
                        copy.Lines.Add(new EnReferenceLine { Seq = x.Seq, DocumentType = x.DocumentType, Discipline = x.Discipline, Title = x.Title, Required = x.Required, TenderStage = x.TenderStage, ClientSubmittal = x.ClientSubmittal, StandardClause = x.StandardClause, Note = x.Note, Units = x.Units });
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
