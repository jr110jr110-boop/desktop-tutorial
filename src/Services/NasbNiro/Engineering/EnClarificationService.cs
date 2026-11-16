using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.EngineeringDtos;
using ViewModels.NasbNiro.Pagination;

namespace Services.NasbNiro.Engineering
{
    public interface IEnClarificationService
    {
        Task<ResponseDto<PagedList<EnClarificationDto>>> GetClarificationsAsync(EnClarificationQuery query, CancellationToken ct);
        Task<ResponseDto<EnClarificationDto>> GetClarificationAsync(int id, CancellationToken ct);
        Task<ResponseDto<EnClarificationDto>> SaveClarificationAsync(EnSaveClarificationDto dto, CancellationToken ct);
        Task<ResponseDto<EnClarificationDto>> ClarificationActionAsync(EnClarificationActionDto dto, CancellationToken ct);
    }

    /// <summary>Clarifications of the client through the commercial unit only: engineering writes the question, commercial sends it and records the answer, engineering closes it or asks again. Engineering can never set the sent or answer fields.</summary>
    public class EnClarificationService : IEnClarificationService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public EnClarificationService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;

        private sealed class Who { public bool Writer, Commercial, Seat; }

        private async Task<Who> WhoAsync(CancellationToken ct) => new Who { Writer = _user.IsInRole(EnRoles.Writers), Commercial = _user.IsInRole(EnRoles.Commercial), Seat = await EnActors.IsSeatAsync(_uow, Me, ct) };

        private static bool CaseAllowsQuestions(EnCaseStatus s) => s <= EnCaseStatus.TenderIssued || s == EnCaseStatus.Awarded || s == EnCaseStatus.InEngineering;

        /// <summary>The one predicate of a new question: the button of the case page and the save of a new clarification.</summary>
        internal static string NewQuestionReason(EnCaseStatus status, bool writer) =>
            !writer ? EnText.NotAllowed : !CaseAllowsQuestions(status) ? "برای این وضعیت پرونده پرسش ثبت نمی‌شود." : null;

        private static string SaveReason(EnClarification x, Who w) =>
            !w.Writer ? EnText.NotAllowed : x != null && x.Status != EnClarStatus.Draft ? "فقط پیش‌نویس ویرایش می‌شود." : null;

        private static string SubmitReason(EnClarification x, Who w) =>
            !w.Writer ? EnText.NotAllowed : w.Seat ? EnText.AiNever : x.Status != EnClarStatus.Draft ? "فقط پیش‌نویس به بازرگانی ارسال می‌شود." : null;

        private static string SendReason(EnClarification x, Who w) =>
            !w.Commercial ? EnText.NotAllowed + " (ارسال به کارفرما فقط با بازرگانی است)" : w.Seat ? EnText.AiNever : x.Status != EnClarStatus.WithCommercial ? "فقط پرسش نزد بازرگانی به کارفرما ارسال می‌شود." : null;

        private static string AnswerReason(EnClarification x, Who w) =>
            !w.Commercial ? EnText.NotAllowed + " (ثبت پاسخ کارفرما فقط با بازرگانی است)" : w.Seat ? EnText.AiNever : x.Status != EnClarStatus.SentToClient ? "فقط پرسش ارسال‌شده به کارفرما پاسخ می‌گیرد." : null;

        private static string CloseReason(EnClarification x, Who w) =>
            !w.Writer ? EnText.NotAllowed : w.Seat ? EnText.AiNever : x.Status != EnClarStatus.Answered ? "فقط پرسش پاسخ‌داده‌شده بسته می‌شود." : null;

        private static string VoidReasonOf(EnClarification x, Who w) =>
            !w.Writer ? EnText.NotAllowed : x.Status != EnClarStatus.Draft && x.Status != EnClarStatus.WithCommercial ? "فقط پیش‌نویس یا پرسش نزد بازرگانی ابطال می‌شود." : null;

        private static List<Guid> Ids(string text) => (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out Guid g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();

        private static string Join(IEnumerable<Guid> ids) => ids == null ? null : string.Join(",", ids.Where(g => g != Guid.Empty).Distinct().Select(g => g.ToString("N")));

        private async Task<List<EnClarificationDto>> DtosAsync(List<EnClarification> rows, CancellationToken ct)
        {
            Who w = await WhoAsync(ct);
            Dictionary<int, string> names = await EnUsers.NamesAsync(_uow, rows.SelectMany(r => new int?[] { r.AskedByUserId, r.SentToClientByUserId, r.AnswerRecordedByUserId, r.ClosedByUserId }), ct);
            List<int> caseIds = rows.Select(r => r.CaseId).Distinct().ToList();
            Dictionary<int, string> cases = await _uow.Set<EnCase>().AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
            return rows.Select(x =>
            {
                string save = SaveReason(x, w), submit = SubmitReason(x, w), send = SendReason(x, w), answer = AnswerReason(x, w), close = CloseReason(x, w), voidR = VoidReasonOf(x, w);
                string reask = !w.Writer ? EnText.NotAllowed : w.Seat ? EnText.AiNever : x.Status != EnClarStatus.Answered ? "فقط پرسش پاسخ‌داده‌شده دوباره پرسیده می‌شود." : null;
                return new EnClarificationDto
                {
                    Id = x.Id, Number = x.Number, CaseId = x.CaseId, CaseNumber = cases.TryGetValue(x.CaseId, out string cn) ? cn : null, FollowsId = x.FollowsId, Question = x.Question, QuestionAttachments = Ids(x.QuestionAttachmentIds),
                    AskedByName = names.Name(x.AskedByUserId), AskedAtText = EnText.Stamp(x.AskedAt), Status = (int)x.Status, StatusText = EnText.Clar(x.Status), SentToClientByName = names.Name(x.SentToClientByUserId), SentToClientAtText = EnText.Stamp(x.SentToClientAt),
                    ClientLetterRef = x.ClientLetterRef, Answer = x.Answer, AnswerAttachments = Ids(x.AnswerAttachmentIds), AnswerRecordedByName = names.Name(x.AnswerRecordedByUserId), AnswerRecordedAtText = EnText.Stamp(x.AnswerRecordedAt),
                    ClosedByName = names.Name(x.ClosedByUserId), ClosedAtText = EnText.Stamp(x.ClosedAt), VoidReason = x.VoidReason, RowVersion = EnConcurrency.Text(x.RowVersion),
                    CanSave = save == null, SaveBlockedReason = save, CanSubmit = submit == null, SubmitBlockedReason = submit, CanSend = send == null, SendBlockedReason = send, CanAnswer = answer == null, AnswerBlockedReason = answer,
                    CanClose = close == null, CloseBlockedReason = close, CanReask = reask == null, ReaskBlockedReason = reask, CanVoid = voidR == null, VoidBlockedReason = voidR,
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<EnClarificationDto>>> GetClarificationsAsync(EnClarificationQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<PagedList<EnClarificationDto>>(EnText.NotAllowed);
            query ??= new EnClarificationQuery();
            IQueryable<EnClarification> q = _uow.Set<EnClarification>().AsNoTracking().Where(x => !x.Deleted);
            if (query.CaseId.HasValue) q = q.Where(x => x.CaseId == query.CaseId.Value);
            if (query.Status.HasValue) { EnClarStatus s = (EnClarStatus)query.Status.Value; q = q.Where(x => x.Status == s); }
            if (query.OnlyOpen) q = q.Where(x => x.Status != EnClarStatus.Closed && x.Status != EnClarStatus.Void);
            return ResponseDtoHelper.Ok(await EnPaging.CutAsync(q.OrderByDescending(x => x.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<EnClarificationDto>> GetClarificationAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(EnRoles.Readers)) return EnText.Bad<EnClarificationDto>(EnText.NotAllowed);
            EnClarification x = await _uow.Set<EnClarification>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.Deleted, ct);
            if (x == null) return EnText.NotFound<EnClarificationDto>("ابهام‌خواهی پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<EnClarification> { x }, ct))[0]);
        }

        private async Task<string> AttachmentProblemAsync(List<Guid> ids, CancellationToken ct)
        {
            if (ids == null || ids.Count == 0) return null;
            if (ids.Count > 20) return "حداکثر ۲۰ پیوست مجاز است.";
            foreach (Guid id in ids)
            {
                string problem = await EnFiles.CheckOwnAsync(_uow, id, Me, ct);
                if (problem != null) return problem;
            }
            return null;
        }

        public async Task<ResponseDto<EnClarificationDto>> SaveClarificationAsync(EnSaveClarificationDto dto, CancellationToken ct)
        {
            Who w = await WhoAsync(ct);
            if (!w.Writer) return EnText.Bad<EnClarificationDto>(EnText.NotAllowed);
            if (dto == null || EnText.Clean(dto.Question) == null) return EnText.Bad<EnClarificationDto>("متن پرسش را بنویسید.");
            string problem = EnText.Over(dto.Question, 4000, "پرسش");
            if (problem != null) return EnText.Bad<EnClarificationDto>(problem);
            DateTimeOffset now = DateTimeOffset.Now;
            EnClarification x;
            if (dto.Id.HasValue)
            {
                x = await _uow.Set<EnClarification>().FirstOrDefaultAsync(c => c.Id == dto.Id.Value && !c.Deleted, ct);
                if (x == null) return EnText.NotFound<EnClarificationDto>("ابهام‌خواهی پیدا نشد.");
                problem = SaveReason(x, w);
                if (problem != null) return EnText.Bad<EnClarificationDto>(problem);
                if (!EnConcurrency.Apply(_uow, x, dto.RowVersion)) return EnText.Bad<EnClarificationDto>(EnText.InvalidData);
            }
            else
            {
                EnCase c = await _uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(k => k.Id == dto.CaseId && !k.Deleted, ct);
                if (c == null) return EnText.NotFound<EnClarificationDto>(EnText.NotFoundCase);
                problem = NewQuestionReason(c.Status, w.Writer);
                if (problem != null) return EnText.Bad<EnClarificationDto>(problem);
                x = new EnClarification { CaseId = c.Id, AskedByUserId = Me, AskedAt = now, Status = EnClarStatus.Draft, CreateDate = now, CreatorUserId = Me };
                x.Number = await EnNumbers.NextAsync(_uow, EnNumbers.Clarification, "ECQ", now.DateTime, ct);
                _uow.Set<EnClarification>().Add(x);
            }
            problem = await AttachmentProblemAsync(dto.QuestionAttachments, ct);
            if (problem != null) return EnText.Bad<EnClarificationDto>(problem);
            x.Question = dto.Question.Trim(); x.QuestionAttachmentIds = Join(dto.QuestionAttachments); x.ModifyDate = now; x.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnClarificationDto>(EnText.Concurrency); }
            return await GetClarificationAsync(x.Id, ct);
        }

        public async Task<ResponseDto<EnClarificationDto>> ClarificationActionAsync(EnClarificationActionDto dto, CancellationToken ct)
        {
            if (dto == null) return EnText.Bad<EnClarificationDto>(EnText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action != "submit" && action != "send" && action != "answer" && action != "close" && action != "reask" && action != "void") return EnText.Bad<EnClarificationDto>("عمل نامعتبر است (submit، send، answer، close، reask یا void).");
            EnClarification x = await _uow.Set<EnClarification>().FirstOrDefaultAsync(c => c.Id == dto.Id && !c.Deleted, ct);
            if (x == null) return EnText.NotFound<EnClarificationDto>("ابهام‌خواهی پیدا نشد.");
            if (!EnConcurrency.Apply(_uow, x, dto.RowVersion)) return EnText.Bad<EnClarificationDto>(EnText.InvalidData);
            Who w = await WhoAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string why;
            int resultId = x.Id;
            switch (action)
            {
                case "submit":
                    why = SubmitReason(x, w);
                    if (why != null) return EnText.Bad<EnClarificationDto>(why);
                    x.Status = EnClarStatus.WithCommercial;
                    break;
                case "send":
                {
                    why = SendReason(x, w);
                    string letter = EnText.Clean(dto.ClientLetterRef);
                    if (why == null && letter == null) why = "شمارهٔ نامهٔ ارسال به کارفرما را بنویسید.";
                    why ??= EnText.Over(letter, 100, "شمارهٔ نامه");
                    if (why != null) return EnText.Bad<EnClarificationDto>(why);
                    x.Status = EnClarStatus.SentToClient; x.ClientLetterRef = letter; x.SentToClientByUserId = Me; x.SentToClientAt = now;
                    break;
                }
                case "answer":
                {
                    why = AnswerReason(x, w);
                    string answer = EnText.Clean(dto.Answer);
                    List<Guid> files = (dto.AnswerAttachments ?? new List<Guid>()).Where(g => g != Guid.Empty).Distinct().ToList();
                    if (why == null && answer == null && files.Count == 0) why = "متن پاسخ یا فایل پاسخ کارفرما را ثبت کنید.";
                    why ??= EnText.Over(answer, 4000, "پاسخ") ?? await AttachmentProblemAsync(files, ct);
                    if (why != null) return EnText.Bad<EnClarificationDto>(why);
                    x.Status = EnClarStatus.Answered; x.Answer = answer; x.AnswerAttachmentIds = Join(files); x.AnswerRecordedByUserId = Me; x.AnswerRecordedAt = now;
                    break;
                }
                case "close":
                    why = CloseReason(x, w);
                    if (why != null) return EnText.Bad<EnClarificationDto>(why);
                    x.Status = EnClarStatus.Closed; x.ClosedByUserId = Me; x.ClosedAt = now;
                    break;
                case "reask":
                {
                    why = !w.Writer ? EnText.NotAllowed : w.Seat ? EnText.AiNever : x.Status != EnClarStatus.Answered ? "فقط پرسش پاسخ‌داده‌شده دوباره پرسیده می‌شود." : null;
                    if (why == null && EnText.Clean(dto.Question) == null) why = "پرسش تازه را بنویسید.";
                    why ??= EnText.Over(dto.Question, 4000, "پرسش");
                    if (why != null) return EnText.Bad<EnClarificationDto>(why);
                    EnCase c = await _uow.Set<EnCase>().AsNoTracking().FirstAsync(k => k.Id == x.CaseId, ct);
                    if (!CaseAllowsQuestions(c.Status)) return EnText.Bad<EnClarificationDto>("برای این وضعیت پرونده پرسش ثبت نمی‌شود.");
                    x.Status = EnClarStatus.Closed; x.ClosedByUserId = Me; x.ClosedAt = now; x.ModifyDate = now; x.ModifierUserId = Me;
                    EnClarification next = new EnClarification { CaseId = x.CaseId, FollowsId = x.Id, Question = dto.Question.Trim(), AskedByUserId = Me, AskedAt = now, Status = EnClarStatus.WithCommercial, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
                    next.Number = await EnNumbers.NextAsync(_uow, EnNumbers.Clarification, "ECQ", now.DateTime, ct);
                    _uow.Set<EnClarification>().Add(next);
                    try { await _uow.SaveChangesAsync(ct); }
                    catch (DbUpdateConcurrencyException) { return EnText.Bad<EnClarificationDto>(EnText.Concurrency); }
                    await SyncAsync(next, ct); await SyncAsync(x, ct);
                    return await GetClarificationAsync(next.Id, ct);
                }
                default:
                    why = VoidReasonOf(x, w) ?? EnText.ReasonShort(EnText.Clean(dto.Note), EnRules.ReasonMin, "دلیل ابطال");
                    if (why != null) return EnText.Bad<EnClarificationDto>(why);
                    x.Status = EnClarStatus.Void; x.VoidReason = EnText.Cut(EnText.Clean(dto.Note), 1000);
                    break;
            }
            x.ModifyDate = now; x.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return EnText.Bad<EnClarificationDto>(EnText.Concurrency); }
            await SyncAsync(x, ct);
            return await GetClarificationAsync(resultId, ct);
        }

        /// <summary>The inbox of the commercial side (send, answer) and of engineering (answer received) follows the state.</summary>
        private async Task SyncAsync(EnClarification x, CancellationToken ct)
        {
            await OfficeGates.SyncAsync(_notify, "EN-CLAR-SEND-" + x.Id, x.Status == EnClarStatus.WithCommercial, "Engineering", nameof(EnClarification), x.Id, "ارسال پرسش مهندسی " + x.Number + " به کارفرما", "/engineering/clarifications/" + x.Id, EnRoles.Commercial, ct, null, OfInboxKind.Task);
            await OfficeGates.SyncAsync(_notify, "EN-CLAR-ANSWER-" + x.Id, x.Status == EnClarStatus.SentToClient, "Engineering", nameof(EnClarification), x.Id, "ثبت پاسخ کارفرما به پرسش " + x.Number, "/engineering/clarifications/" + x.Id, EnRoles.Commercial, ct, null, OfInboxKind.Task);
            await OfficeGates.SyncAsync(_notify, "EN-CLAR-CLOSE-" + x.Id, x.Status == EnClarStatus.Answered, "Engineering", nameof(EnClarification), x.Id, "بررسی پاسخ کارفرما به پرسش " + x.Number, "/engineering/clarifications/" + x.Id, EnRoles.Writers, ct, null, OfInboxKind.Task);
        }
    }
}
