using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
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

    /// <summary>Customer satisfaction surveys (ISO 9001 9.1.2): the system drafts them, a person sends one and records the scores the customer gave.</summary>
    public class SvSurveyService : ISvSurveyService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public SvSurveyService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        private async Task<List<SlSurveyDto>> DtosAsync(List<SlSurvey> rows, CancellationToken ct)
        {
            List<int> customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            return rows.Select(r => new SlSurveyDto
            {
                Id = r.Id, Number = r.Number, Kind = r.Kind, KindText = SvText.Survey(r.Kind), CustomerId = r.CustomerId, CustomerName = customers.GetValueOrDefault(r.CustomerId), ContractId = r.ContractId, ShipmentId = r.ShipmentId,
                ServiceOrderId = r.ServiceOrderId, DraftedOn = r.DraftedOn, DraftedOnText = SlText.Date(r.DraftedOn), Status = r.Status, StatusText = SvText.SurveyStatus(r.Status), SentAtText = SlText.Stamp(r.SentAt),
                AnsweredAtText = SlText.Stamp(r.AnsweredAt), Comments = r.Comments, AverageScore = r.AverageScore,
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
            if (!_user.IsInRole("SERVICE,SALES")) return Bad<SlSurveyDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlSurveyDto>(SlText.InvalidData);
            SlSurvey row = await _uow.Set<SlSurvey>().Include(s => s.Answers).FirstOrDefaultAsync(s => s.Id == dto.Id && !s.Deleted, ct);
            if (row == null) return SlText.NotFound<SlSurveyDto>("نظرسنجی پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlSurveyDto>(SlText.InvalidData);
            DateTimeOffset now = DateTimeOffset.Now;
            switch ((dto.Action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "send":
                    if (row.Status != SlSurveyStatus.Drafted) return Bad<SlSurveyDto>("فقط نظرسنجی پیش‌نویس ارسال می‌شود.");
                    row.Status = SlSurveyStatus.Sent; row.SentAt = now;
                    break;
                case "skip":
                    if (row.Status != SlSurveyStatus.Drafted && row.Status != SlSurveyStatus.Sent) return Bad<SlSurveyDto>("نظرسنجی پاسخ‌داده‌شده رد نمی‌شود.");
                    row.Status = SlSurveyStatus.Skipped;
                    break;
                default:
                    return Bad<SlSurveyDto>("عملیات نامعتبر است.");
            }
            row.ModifyDate = now; row.ModifierUserId = _user.UserId;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SlSurveyDto>(SlText.Concurrency); }
            return await GetSurveyAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SlSurveyDto>> AnswerAsync(SlSurveyAnswerSaveDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole("SERVICE,SALES")) return Bad<SlSurveyDto>(SlText.NotAllowed);
            if (dto == null || dto.Scores == null || dto.Scores.Count == 0) return Bad<SlSurveyDto>("امتیاز حداقل یک پرسش را وارد کنید.");
            if (dto.Scores.Any(s => !SvSurveyMath.ValidScore(s.Score))) return Bad<SlSurveyDto>("امتیاز هر پرسش باید بین ۱ تا ۵ باشد.");
            if (dto.Scores.GroupBy(s => s.RowNumber).Any(g => g.Count() > 1)) return Bad<SlSurveyDto>("هر پرسش فقط یک امتیاز دارد.");
            SlSurvey row = await _uow.Set<SlSurvey>().Include(s => s.Answers).FirstOrDefaultAsync(s => s.Id == dto.Id && !s.Deleted, ct);
            if (row == null) return SlText.NotFound<SlSurveyDto>("نظرسنجی پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlSurveyDto>(SlText.InvalidData);
            if (row.Status != SlSurveyStatus.Sent) return Bad<SlSurveyDto>("پاسخ فقط برای نظرسنجیِ ارسال‌شده ثبت می‌شود.");
            foreach (SlSurveyScoreDto s in dto.Scores)
            {
                SlSurveyAnswer a = row.Answers.FirstOrDefault(x => x.RowNumber == s.RowNumber);
                if (a == null) return Bad<SlSurveyDto>($"پرسش شماره {s.RowNumber} پیدا نشد.");
                a.Score = s.Score;
            }
            DateTimeOffset now = DateTimeOffset.Now;
            row.AverageScore = SvSurveyMath.Average(row.Answers.Where(a => a.Score.HasValue).Select(a => a.Score.Value));
            row.Comments = SlText.Clean(dto.Comments); row.Status = SlSurveyStatus.Answered; row.AnsweredAt = now; row.ModifyDate = now; row.ModifierUserId = _user.UserId;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<SlSurveyDto>(SlText.Concurrency); }
            return await GetSurveyAsync(row.Id, ct);
        }
    }
}
