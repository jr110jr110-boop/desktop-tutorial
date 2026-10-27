using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ServiceDtos;

namespace Services.NasbNiro.AfterSales
{
    /// <summary>Drafting of the satisfaction surveys (shared by the closing of an order and the nightly job).</summary>
    internal static class SvSurveyDraft
    {
        private static SlSurvey New(SlSurveyKind kind, int customerId, DateTime date, string number, int userId)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            var survey = new SlSurvey { Number = number, Kind = kind, CustomerId = customerId, DraftedOn = date.Date, Status = SlSurveyStatus.Drafted, CreateDate = now, CreatorUserId = userId, ModifyDate = now, ModifierUserId = userId };
            int n = 0;
            foreach (string q in SvSurveyMath.Questions) survey.Answers.Add(new SlSurveyAnswer { RowNumber = ++n, Question = q });
            return survey;
        }

        /// <summary>The survey of a closed service order; one per order.</summary>
        public static async Task<bool> ForOrderAsync(IUnitOfWork uow, SvServiceOrder order, DateTime date, int userId, CancellationToken ct)
        {
            if (!order.CustomerId.HasValue) return false;
            if (await uow.Set<SlSurvey>().AnyAsync(s => s.ServiceOrderId == order.Id && !s.Deleted, ct)) return false;
            SlSurvey survey = New(SlSurveyKind.Service, order.CustomerId.Value, date, await InventoryNumbers.NextNumberAsync(uow, SvNumbers.Survey, "SSV", date, ct), userId);
            survey.ServiceOrderId = order.Id; survey.ContractId = order.ContractId;
            uow.Set<SlSurvey>().Add(survey);
            return true;
        }

        /// <summary>The delivery survey of a shipment whose handover is old enough; one per shipment.</summary>
        public static async Task<bool> ForShipmentAsync(IUnitOfWork uow, int shipmentId, int customerId, int? contractId, DateTime date, int userId, CancellationToken ct)
        {
            if (await uow.Set<SlSurvey>().AnyAsync(s => s.ShipmentId == shipmentId && !s.Deleted, ct)) return false;
            SlSurvey survey = New(SlSurveyKind.Delivery, customerId, date, await InventoryNumbers.NextNumberAsync(uow, SvNumbers.Survey, "SSV", date, ct), userId);
            survey.ShipmentId = shipmentId; survey.ContractId = contractId;
            uow.Set<SlSurvey>().Add(survey);
            return true;
        }
    }

    public interface ISvSurveyService
    {
        Task<ResponseDto<PagedList<SlSurveyDto>>> GetSurveysAsync(SlSurveyQuery query, CancellationToken ct);
        Task<ResponseDto<SlSurveyDto>> GetSurveyAsync(int id, CancellationToken ct);
        Task<ResponseDto<SlSurveyDto>> SurveyActionAsync(SlSurveyActionDto dto, CancellationToken ct);
        Task<ResponseDto<SlSurveyDto>> AnswerAsync(SlSurveyAnswerSaveDto dto, CancellationToken ct);
    }

    /// <summary>
    /// Customer satisfaction surveys (ISO 9001 9.1.2): the system drafts them, a person sends one and records the scores the customer gave.
    /// R1p D25: the people who type the scores in are the people the scores are about, so (1) the answer is marked with their role and stays unverified until QA checks it with the customer (QA only; never the same user),
    /// (2) a survey is skipped only with a reason, (3) a survey is "answered" only when every question has a score, (4) a score below Service:LowScoreThreshold (default 3, ★ provisional) opens a customer complaint draft, once per survey.
    /// </summary>
    public class SvSurveyService : ISvSurveyService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IConfiguration _configuration;

        public SvSurveyService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null, IConfiguration configuration = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
            _configuration = configuration;
        }

        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        private async Task<List<SlSurveyDto>> DtosAsync(List<SlSurvey> rows, CancellationToken ct)
        {
            List<int> customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            List<int> complaintIds = rows.Where(r => r.ComplaintId.HasValue).Select(r => r.ComplaintId.Value).Distinct().ToList();
            Dictionary<int, string> complaints = complaintIds.Count == 0 ? new Dictionary<int, string>()
                : await _uow.Set<CustomerComplaint>().AsNoTracking().Where(c => complaintIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
            return rows.Select(r => new SlSurveyDto
            {
                Id = r.Id, Number = r.Number, Kind = r.Kind, KindText = SvText.Survey(r.Kind), CustomerId = r.CustomerId, CustomerName = customers.GetValueOrDefault(r.CustomerId), ContractId = r.ContractId, ShipmentId = r.ShipmentId,
                ServiceOrderId = r.ServiceOrderId, DraftedOn = r.DraftedOn, DraftedOnText = SlText.Date(r.DraftedOn), Status = r.Status, StatusText = SvText.SurveyStatus(r.Status), SentAtText = SlText.Stamp(r.SentAt),
                AnsweredAtText = SlText.Stamp(r.AnsweredAt), Comments = r.Comments, AverageScore = r.AverageScore,
                SkipReason = r.SkipReason, RecordedByRole = r.RecordedByRole, Verified = r.Verified, VerifiedAtText = SlText.Stamp(r.VerifiedAt), VerifyNote = r.VerifyNote,
                ComplaintId = r.ComplaintId, ComplaintNumber = r.ComplaintId.HasValue ? complaints.GetValueOrDefault(r.ComplaintId.Value) : null,
                Answers = r.Answers.OrderBy(a => a.RowNumber).Select(a => new SlSurveyAnswerDto { Id = a.Id, RowNumber = a.RowNumber, Question = a.Question, Score = a.Score }).ToList(), RowVersion = SlConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlSurveyDto>>> GetSurveysAsync(SlSurveyQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<PagedList<SlSurveyDto>>(SlText.NotAllowed);
            query ??= new SlSurveyQuery();
            IQueryable<SlSurvey> q = _uow.Set<SlSurvey>().AsNoTracking().Where(s => !s.Deleted);
            if (query.Status.HasValue) q = q.Where(s => s.Status == query.Status.Value);
            if (query.Kind.HasValue) q = q.Where(s => s.Kind == query.Kind.Value);
            if (query.CustomerId.HasValue) q = q.Where(s => s.CustomerId == query.CustomerId.Value);
            if (query.Verified.HasValue) q = q.Where(s => s.Status == SlSurveyStatus.Answered && s.Verified == query.Verified.Value);
            string text = SlText.Clean(query.Contains);
            if (text != null) q = q.Where(s => s.Number.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(s => s.Id), query.PageNumber, query.PageSize, async rows =>
            {
                List<int> ids = rows.Select(r => r.Id).ToList();
                List<SlSurvey> full = await _uow.Set<SlSurvey>().AsNoTracking().Include(s => s.Answers).Where(s => ids.Contains(s.Id)).ToListAsync(ct);
                return await DtosAsync(ids.Select(id => full.First(f => f.Id == id)).ToList(), ct);
            }, ct));
        }

        public async Task<ResponseDto<SlSurveyDto>> GetSurveyAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SvRoles.Readers)) return Bad<SlSurveyDto>(SlText.NotAllowed);
            SlSurvey row = await _uow.Set<SlSurvey>().AsNoTracking().Include(s => s.Answers).FirstOrDefaultAsync(s => s.Id == id && !s.Deleted, ct);
            if (row == null) return SlText.NotFound<SlSurveyDto>("نظرسنجی پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<SlSurvey> { row }, ct))[0]);
        }

        public async Task<ResponseDto<SlSurveyDto>> SurveyActionAsync(SlSurveyActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SlSurveyDto>(SlText.InvalidData);
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            // sending and skipping stay with the service and the sales; the verification of the answers is QA's alone (R1p D25)
            if (!_user.IsInRole(action == "verify" ? SvRoles.SurveyVerifiers : "SERVICE,SALES")) return Bad<SlSurveyDto>(SlText.NotAllowed);
            SlSurvey row = await _uow.Set<SlSurvey>().Include(s => s.Answers).FirstOrDefaultAsync(s => s.Id == dto.Id && !s.Deleted, ct);
            if (row == null) return SlText.NotFound<SlSurveyDto>("نظرسنجی پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlSurveyDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (action)
            {
                case "send":
                    if (row.Status != SlSurveyStatus.Drafted) return Bad<SlSurveyDto>("فقط نظرسنجی پیش‌نویس ارسال می‌شود.");
                    row.Status = SlSurveyStatus.Sent; row.SentAt = now;
                    break;
                case "skip":
                    if (row.Status != SlSurveyStatus.Drafted && row.Status != SlSurveyStatus.Sent) return Bad<SlSurveyDto>("نظرسنجی پاسخ‌داده‌شده رد نمی‌شود.");
                    string reason = SlText.Clean(dto.Reason);
                    if (reason == null) return Bad<SlSurveyDto>("دلیل رد نظرسنجی را وارد کنید.");
                    string reasonTooLong = SvLimits.TooLong(reason, SvLimits.Text, "دلیل رد نظرسنجی");
                    if (reasonTooLong != null) return Bad<SlSurveyDto>(reasonTooLong);
                    row.Status = SlSurveyStatus.Skipped; row.SkipReason = reason;
                    break;
                case "verify":
                    if (row.Status != SlSurveyStatus.Answered) return Bad<SlSurveyDto>("فقط پاسخ‌های نظرسنجی پاسخ‌داده‌شده تأیید می‌شود.");
                    if (row.Verified) return Bad<SlSurveyDto>("پاسخ‌های این نظرسنجی قبلاً تأیید شده است.");
                    if (row.AnsweredByUserId == _user.UserId) return Bad<SlSurveyDto>(SlText.SameUser);
                    string note = SlText.Clean(dto.Note);
                    if (note == null) return Bad<SlSurveyDto>("بنویسید با مشتری چه چیزی بررسی شد.");
                    string noteTooLong = SvLimits.TooLong(note, SvLimits.Text, "یادداشت تأیید");
                    if (noteTooLong != null) return Bad<SlSurveyDto>(noteTooLong);
                    row.Verified = true; row.VerifiedByUserId = _user.UserId; row.VerifiedAt = now; row.VerifyNote = note;
                    break;
                default:
                    return Bad<SlSurveyDto>("عملیات نامعتبر است.");
            }
            row.ModifyDate = now; row.ModifierUserId = _user.UserId;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SlSurveyDto>(SlText.Concurrency); }
            await SvGates.SurveyAsync(_notify, row, ct);
            return await GetSurveyAsync(row.Id, ct);
        }

        /// <summary>
        /// R1p D25: a score below the threshold opens one customer complaint draft (received, not yet classified) that points to the survey. The complaint is added to the same save as the answer,
        /// so the survey and its complaint are written together; a survey that already has one never gets a second.
        /// </summary>
        private async Task OpenComplaintIfLowAsync(SlSurvey row, DateTimeOffset now, CancellationToken ct)
        {
            if (row.ComplaintId.HasValue || row.Complaint != null) return;
            int threshold = SvSurveyMath.Threshold(await SvSettings.TextAsync(_uow, SvSettingKeys.LowScoreThreshold, ct), _configuration?[SvSurveyMath.ConfigKey]);
            var low = SvSurveyMath.LowScores(row.Answers.Select(a => (a.RowNumber, a.Question, a.Score)), threshold);
            if (low.Count == 0) return;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(_uow, ct);
            DateTime received = DateTime.Now;
            int? workOrderId = null;
            if (row.ServiceOrderId.HasValue)
            {
                int? unitId = await _uow.Set<SvServiceOrder>().AsNoTracking().Where(o => o.Id == row.ServiceOrderId.Value).Select(o => (int?)o.UnitId).FirstOrDefaultAsync(ct);
                if (unitId.HasValue) workOrderId = await _uow.Set<SvInstalledUnit>().AsNoTracking().Where(u => u.Id == unitId.Value).Select(u => u.WorkOrderId).FirstOrDefaultAsync(ct);
            }
            string scores = string.Join("؛ ", low.Select(l => $"«{l.Question}» امتیاز {l.Score} از ۵"));
            row.Complaint = new CustomerComplaint
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.Complaint, "CC", received, ct), Status = ComplaintStatus.Received, CreateDate = now, CreatorUserId = _user.UserId,
                ContractId = row.ContractId, WorkOrderId = workOrderId, ReceivedDate = received, Channel = "نظرسنجی رضایت مشتری",
                Description = SvLimits.Cut($"[{row.Number}] امتیاز پایین در نظرسنجی رضایت مشتری (آستانه‌ی {threshold}): {scores}." + (SlText.Clean(row.Comments) == null ? string.Empty : " توضیح مشتری: " + SlText.Clean(row.Comments)), SvLimits.LongText),
                Severity = NcrSeverity.NotClassified, ResponseDue = received.AddDays((int)settings[QualitySettingKeys.ComplaintResponseDays]), ModifyDate = now, ModifierUserId = _user.UserId,
            };
            _uow.Set<CustomerComplaint>().Add(row.Complaint);
        }

        public async Task<ResponseDto<SlSurveyDto>> AnswerAsync(SlSurveyAnswerSaveDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole("SERVICE,SALES")) return Bad<SlSurveyDto>(SlText.NotAllowed);
            if (dto == null || dto.Scores == null || dto.Scores.Count == 0) return Bad<SlSurveyDto>("امتیاز حداقل یک پرسش را وارد کنید.");
            if (dto.Scores.Any(s => !SvSurveyMath.ValidScore(s.Score))) return Bad<SlSurveyDto>("امتیاز هر پرسش باید بین ۱ تا ۵ باشد.");
            if (dto.Scores.GroupBy(s => s.RowNumber).Any(g => g.Count() > 1)) return Bad<SlSurveyDto>("هر پرسش فقط یک امتیاز دارد.");
            string commentsTooLong = SvLimits.TooLong(SlText.Clean(dto.Comments), SvLimits.LongText, "توضیحات نظرسنجی");
            if (commentsTooLong != null) return Bad<SlSurveyDto>(commentsTooLong);
            SlSurvey row = await _uow.Set<SlSurvey>().Include(s => s.Answers).FirstOrDefaultAsync(s => s.Id == dto.Id && !s.Deleted, ct);
            if (row == null) return SlText.NotFound<SlSurveyDto>("نظرسنجی پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlSurveyDto>(SlText.InvalidData);
            if (row.Status != SlSurveyStatus.Sent) return Bad<SlSurveyDto>("پاسخ فقط برای نظرسنجیِ ارسال‌شده ثبت می‌شود.");
            // the scores are merged on a copy first: a refused answer leaves the survey as it was
            Dictionary<int, int?> merged = row.Answers.ToDictionary(a => a.RowNumber, a => a.Score);
            foreach (SlSurveyScoreDto s in dto.Scores)
            {
                if (!merged.ContainsKey(s.RowNumber)) return Bad<SlSurveyDto>($"پرسش شماره {s.RowNumber} پیدا نشد.");
                merged[s.RowNumber] = s.Score;
            }
            // R1p D25: "answered" means every question has a score; a survey with one answer out of four would otherwise carry the average of that one score
            if (!SvSurveyMath.AllAnswered(merged.Values))
                return Bad<SlSurveyDto>("نظرسنجی فقط وقتی پاسخ‌داده‌شده است که همه‌ی پرسش‌ها امتیاز داشته باشد؛ پرسش‌های بدون امتیاز: " + string.Join("، ", merged.Where(m => !m.Value.HasValue).Select(m => m.Key).OrderBy(n => n)) + ".");
            foreach (SlSurveyAnswer a in row.Answers) a.Score = merged[a.RowNumber];
            DateTimeOffset now = DateTimeOffset.Now;
            row.AverageScore = SvSurveyMath.Average(row.Answers.Where(a => a.Score.HasValue).Select(a => a.Score.Value));
            row.Comments = SlText.Clean(dto.Comments); row.Status = SlSurveyStatus.Answered; row.AnsweredAt = now; row.ModifyDate = now; row.ModifierUserId = _user.UserId;
            // who typed it in (the person the scores are about) and the check still to come from QA
            row.AnsweredByUserId = _user.UserId; row.RecordedByRole = _user.IsInRole("SERVICE") ? "SERVICE" : "SALES"; row.Verified = false;
            await OpenComplaintIfLowAsync(row, now, ct);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SlSurveyDto>(SlText.Concurrency); }
            await SvGates.SurveyAsync(_notify, row, ct);
            return await GetSurveyAsync(row.Id, ct);
        }
    }
}
