using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Sales;
using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Sales
{
    public interface ISlPipelineService
    {
        Task<ResponseDto<PagedList<SlInquiryDto>>> GetInquiriesAsync(SlInquiryQuery query, CancellationToken ct);
        Task<ResponseDto<SlInquiryDto>> GetInquiryAsync(int id, CancellationToken ct);
        Task<ResponseDto<SlInquiryDto>> SaveInquiryAsync(SlSaveInquiryDto dto, CancellationToken ct);
        Task<ResponseDto<SlInquiryDto>> InquiryActionAsync(SlInquiryActionDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<SlBidDecisionDto>>> GetBidDecisionsAsync(SlBidDecisionQuery query, CancellationToken ct);
        Task<ResponseDto<SlBidDecisionDto>> GetBidDecisionAsync(int inquiryId, CancellationToken ct);
        Task<ResponseDto<SlBidDecisionDto>> SaveBidDecisionAsync(SlSaveBidDecisionDto dto, CancellationToken ct);
        Task<ResponseDto<SlBidDecisionDto>> SubmitBidDecisionAsync(SlBidActionDto dto, CancellationToken ct);
        Task<ResponseDto<SlBidDecisionDto>> DecideBidAsync(SlBidActionDto dto, CancellationToken ct);
    }

    /// <summary>Inquiries (RFQ register) and the bid / no-bid gate: sales prepares the checklist, only the CEO decides.</summary>
    public class SlPipelineService : ISlPipelineService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public SlPipelineService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        /// <summary>How far ahead the production load report is read for the capacity item (one year of weeks at most).</summary>
        private const int MaxCapacityWeeks = 104;

        // ================================================================================== inquiries

        private async Task<List<SlInquiryDto>> InquiryDtosAsync(List<SlInquiry> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(r => r.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            return rows.Select(r => new SlInquiryDto
            {
                Id = r.Id, Number = r.Number, ReceivedDate = r.ReceivedDate, ReceivedDateText = SlText.Date(r.ReceivedDate), CustomerId = r.CustomerId,
                CustomerName = customers.TryGetValue(r.CustomerId, out string n) ? n : null, ContactId = r.ContactId, Channel = r.Channel, CustomerReference = r.CustomerReference,
                Title = r.Title, ProductType = r.ProductType, ProductTypeText = SlText.Product(r.ProductType), Quantity = r.Quantity, RequiredDeliveryDate = r.RequiredDeliveryDate,
                RequiredDeliveryDateText = SlText.Date(r.RequiredDeliveryDate), ProposalDueDate = r.ProposalDueDate, ProposalDueDateText = SlText.Date(r.ProposalDueDate),
                AttachmentIds = r.AttachmentIds, Status = r.Status, StatusText = SlText.InquiryStatus(r.Status), TenderId = r.TenderId, Note = r.Note, CancelReason = r.CancelReason,
                RowVersion = SlConcurrency.Text(r.RowVersion),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlInquiryDto>>> GetInquiriesAsync(SlInquiryQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlInquiryDto>>(SlText.NotAllowed);
            query ??= new SlInquiryQuery();
            IQueryable<SlInquiry> q = _uow.Set<SlInquiry>().AsNoTracking().Where(i => !i.Deleted);
            if (query.Status.HasValue) q = q.Where(i => i.Status == query.Status.Value);
            if (query.CustomerId.HasValue) q = q.Where(i => i.CustomerId == query.CustomerId.Value);
            if (query.ProductType.HasValue) q = q.Where(i => i.ProductType == query.ProductType.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(i => i.Number.Contains(text) || i.Title.Contains(text) || i.CustomerReference.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(i => i.ReceivedDate).ThenByDescending(i => i.Id), query.PageNumber, query.PageSize, rows => InquiryDtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<SlInquiryDto>> GetInquiryAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<SlInquiryDto>(SlText.NotAllowed);
            SlInquiry row = await _uow.Set<SlInquiry>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && !i.Deleted, ct);
            if (row == null) return SlText.NotFound<SlInquiryDto>("استعلام پیدا نشد.");
            return ResponseDtoHelper.Ok((await InquiryDtosAsync(new List<SlInquiry> { row }, ct))[0]);
        }

        public async Task<ResponseDto<SlInquiryDto>> SaveInquiryAsync(SlSaveInquiryDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlInquiryDto>(SlText.NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<SlInquiryDto>("عنوان استعلام را وارد کنید.");
            if (!Enum.IsDefined(typeof(SlProductType), dto.ProductType)) return Bad<SlInquiryDto>(SlText.InvalidData);
            if (dto.Quantity <= 0) return Bad<SlInquiryDto>("تعداد باید بیشتر از صفر باشد.");
            if (!await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == dto.CustomerId, ct)) return Bad<SlInquiryDto>("مشتری پیدا نشد.");
            if (dto.ContactId.HasValue && !await _uow.Set<SlContact>().AnyAsync(c => c.Id == dto.ContactId.Value && c.CustomerId == dto.CustomerId && !c.Deleted, ct)) return Bad<SlInquiryDto>("مخاطب انتخاب‌شده از این مشتری نیست.");
            DateTime received = (dto.ReceivedDate ?? DateTime.Today).Date;
            if (dto.ProposalDueDate.HasValue && dto.ProposalDueDate.Value.Date < received) return Bad<SlInquiryDto>("مهلت ارسال پیشنهاد نمی‌تواند قبل از تاریخ دریافت استعلام باشد.");
            if (dto.RequiredDeliveryDate.HasValue && dto.RequiredDeliveryDate.Value.Date < received) return Bad<SlInquiryDto>("تاریخ تحویل درخواستی نمی‌تواند قبل از تاریخ دریافت استعلام باشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            SlInquiry row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlInquiry>().FirstOrDefaultAsync(i => i.Id == dto.Id.Value && !i.Deleted, ct);
                if (row == null) return SlText.NotFound<SlInquiryDto>("استعلام پیدا نشد.");
                if (row.Status != SlInquiryStatus.Received && row.Status != SlInquiryStatus.UnderReview && row.Status != SlInquiryStatus.Bidding)
                    return Bad<SlInquiryDto>($"استعلام در وضعیت «{SlText.InquiryStatus(row.Status)}» قابل ویرایش نیست.");
                if (row.CustomerId != dto.CustomerId && row.Status == SlInquiryStatus.Bidding) return Bad<SlInquiryDto>("مشتری استعلامی که در حال تهیه پیشنهاد است تغییر نمی‌کند.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlInquiryDto>(SlText.InvalidData);
            }
            else
            {
                row = new SlInquiry { CreateDate = now, CreatorUserId = Me, Status = SlInquiryStatus.Received };
                row.Number = await InventoryNumbers.NextNumberAsync(_uow, SlNumbers.Inquiry, "INQ", received, ct);
                _uow.Set<SlInquiry>().Add(row);
            }
            row.ReceivedDate = received; row.CustomerId = dto.CustomerId; row.ContactId = dto.ContactId; row.Channel = Clean(dto.Channel); row.CustomerReference = Clean(dto.CustomerReference);
            row.Title = dto.Title.Trim(); row.ProductType = dto.ProductType; row.Quantity = dto.Quantity; row.RequiredDeliveryDate = dto.RequiredDeliveryDate?.Date;
            row.ProposalDueDate = dto.ProposalDueDate?.Date; row.AttachmentIds = Clean(dto.AttachmentIds); row.Note = Clean(dto.Note); row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlInquiryDto>(SlText.Concurrency);
            }
            return await GetInquiryAsync(row.Id, ct);
        }

        public async Task<ResponseDto<SlInquiryDto>> InquiryActionAsync(SlInquiryActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlInquiryDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlInquiryDto>(SlText.InvalidData);
            SlInquiry row = await _uow.Set<SlInquiry>().FirstOrDefaultAsync(i => i.Id == dto.Id && !i.Deleted, ct);
            if (row == null) return SlText.NotFound<SlInquiryDto>("استعلام پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlInquiryDto>(SlText.InvalidData);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            DateTimeOffset now = DateTimeOffset.Now;
            if (action == "review")
            {
                if (row.Status != SlInquiryStatus.Received) return Bad<SlInquiryDto>($"فقط استعلام «دریافت‌شده» وارد بررسی می‌شود؛ وضعیت فعلی: {SlText.InquiryStatus(row.Status)}.");
                row.Status = SlInquiryStatus.UnderReview;
            }
            else if (action == "cancel")
            {
                if (Clean(dto.Reason) == null) return Bad<SlInquiryDto>("دلیل لغو استعلام را بنویسید.");
                if (row.Status == SlInquiryStatus.Submitted || row.Status == SlInquiryStatus.Won || row.Status == SlInquiryStatus.Lost || row.Status == SlInquiryStatus.Cancelled)
                    return Bad<SlInquiryDto>($"استعلام در وضعیت «{SlText.InquiryStatus(row.Status)}» لغو نمی‌شود.");
                if (await _uow.Set<SlProposal>().AnyAsync(p => p.InquiryId == row.Id && !p.Deleted && (p.Status == SlProposalStatus.Draft || p.Status == SlProposalStatus.Approved), ct))
                    return Bad<SlInquiryDto>("ابتدا پیشنهادهای باز این استعلام را لغو کنید.");
                row.Status = SlInquiryStatus.Cancelled;
                row.CancelReason = Clean(dto.Reason);
            }
            else return Bad<SlInquiryDto>("عمل نامعتبر است (review یا cancel).");
            row.ModifyDate = now;
            row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlInquiryDto>(SlText.Concurrency);
            }
            return await GetInquiryAsync(row.Id, ct);
        }

        // ================================================================================== bid decision

        private async Task<List<SlBidDecisionDto>> BidDtosAsync(List<SlBidDecision> rows, CancellationToken ct)
        {
            List<int> ids = rows.Select(r => r.InquiryId).ToList();
            List<SlInquiry> inquiries = await _uow.Set<SlInquiry>().AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
            List<int> customerIds = inquiries.Select(i => i.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.DecidedByUserId), ct);
            return rows.Select(r =>
            {
                SlInquiry i = inquiries.FirstOrDefault(x => x.Id == r.InquiryId);
                return new SlBidDecisionDto
                {
                    Id = r.Id, InquiryId = r.InquiryId, InquiryNumber = i?.Number, InquiryTitle = i?.Title, CustomerName = i != null && customers.TryGetValue(i.CustomerId, out string n) ? n : null,
                    Status = r.Status, TechnicalCapabilityOk = r.TechnicalCapabilityOk, TechnicalNote = r.TechnicalNote, CapacityOverloadWeeks = r.CapacityOverloadWeeks,
                    CapacityCheckedUntil = r.CapacityCheckedUntil, CapacityCheckedUntilText = SlText.Date(r.CapacityCheckedUntil), CapacityNote = r.CapacityNote,
                    CommercialRiskReviewed = r.CommercialRiskReviewed, CommercialRiskNote = r.CommercialRiskNote, PaymentTermsReviewed = r.PaymentTermsReviewed, PaymentTermsNote = r.PaymentTermsNote,
                    GuaranteesReviewed = r.GuaranteesReviewed, GuaranteesNote = r.GuaranteesNote, SanctionCheck = r.SanctionCheck, SanctionNote = r.SanctionNote,
                    ExpectedMarginPercent = r.ExpectedMarginPercent, SubmittedAt = r.SubmittedAt, SubmittedAtText = SlText.Stamp(r.SubmittedAt), DecidedAt = r.DecidedAt,
                    DecidedAtText = SlText.Stamp(r.DecidedAt), DecidedByUserId = r.DecidedByUserId, DecidedByName = users.Name(r.DecidedByUserId), DecisionReason = r.DecisionReason,
                    TenderId = i?.TenderId, RowVersion = SlConcurrency.Text(r.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlBidDecisionDto>>> GetBidDecisionsAsync(SlBidDecisionQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<PagedList<SlBidDecisionDto>>(SlText.NotAllowed);
            query ??= new SlBidDecisionQuery();
            IQueryable<SlBidDecision> q = _uow.Set<SlBidDecision>().AsNoTracking().Where(d => !d.Deleted);
            if (query.Status.HasValue) q = q.Where(d => d.Status == query.Status.Value);
            if (query.InquiryId.HasValue) q = q.Where(d => d.InquiryId == query.InquiryId.Value);
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(d => d.Id), query.PageNumber, query.PageSize, rows => BidDtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<SlBidDecisionDto>> GetBidDecisionAsync(int inquiryId, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Readers)) return Bad<SlBidDecisionDto>(SlText.NotAllowed);
            SlBidDecision row = await _uow.Set<SlBidDecision>().AsNoTracking().FirstOrDefaultAsync(d => d.InquiryId == inquiryId && !d.Deleted, ct);
            if (row == null) return SlText.NotFound<SlBidDecisionDto>("برای این استعلام هنوز چک‌لیست تصمیم‌گیری ثبت نشده است.");
            return ResponseDtoHelper.Ok((await BidDtosAsync(new List<SlBidDecision> { row }, ct))[0]);
        }

        /// <summary>The capacity item of the checklist: the weeks above 100 % of the capacity between today and the required delivery, read from the production load report.</summary>
        private async Task ReadCapacityAsync(SlBidDecision d, SlInquiry inquiry, CancellationToken ct)
        {
            if (!inquiry.RequiredDeliveryDate.HasValue)
            {
                d.CapacityOverloadWeeks = null;
                d.CapacityCheckedUntil = null;
                d.CapacityCheckedAt = null;
                return;
            }
            DateTime from = DateTime.Today;
            DateTime until = inquiry.RequiredDeliveryDate.Value.Date;
            int weeks = until <= from ? 1 : Math.Min(MaxCapacityWeeks, (int)Math.Ceiling((until - from).TotalDays / 7d) + 1);
            ViewModels.NasbNiro.ProductionDtos.LoadReportDto report = await MfScheduleService.LoadAsync(_uow, from, weeks, 100m, null, ct);
            d.CapacityOverloadWeeks = report.Overloads.Where(c => c.WeekStart <= until).Select(c => c.WeekStart).Distinct().Count();
            d.CapacityCheckedUntil = until;
            d.CapacityCheckedAt = DateTimeOffset.Now;
        }

        public async Task<ResponseDto<SlBidDecisionDto>> SaveBidDecisionAsync(SlSaveBidDecisionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlBidDecisionDto>("چک‌لیست تصمیم‌گیری را فروش آماده می‌کند.");
            if (dto == null) return Bad<SlBidDecisionDto>(SlText.InvalidData);
            if (!Enum.IsDefined(typeof(SlSanctionCheck), dto.SanctionCheck)) return Bad<SlBidDecisionDto>(SlText.InvalidData);
            if (dto.ExpectedMarginPercent.HasValue && (dto.ExpectedMarginPercent.Value <= -100m || dto.ExpectedMarginPercent.Value >= 100m)) return Bad<SlBidDecisionDto>("حاشیه سود مورد انتظار باید بین ۱۰۰- و ۱۰۰ درصد باشد.");
            SlInquiry inquiry = await _uow.Set<SlInquiry>().FirstOrDefaultAsync(i => i.Id == dto.InquiryId && !i.Deleted, ct);
            if (inquiry == null) return SlText.NotFound<SlBidDecisionDto>("استعلام پیدا نشد.");
            if (inquiry.Status != SlInquiryStatus.Received && inquiry.Status != SlInquiryStatus.UnderReview)
                return Bad<SlBidDecisionDto>($"برای استعلام در وضعیت «{SlText.InquiryStatus(inquiry.Status)}» چک‌لیست جدید ثبت نمی‌شود.");
            DateTimeOffset now = DateTimeOffset.Now;
            SlBidDecision row = await _uow.Set<SlBidDecision>().FirstOrDefaultAsync(d => d.InquiryId == inquiry.Id && !d.Deleted, ct);
            if (row == null)
            {
                row = new SlBidDecision { InquiryId = inquiry.Id, CreateDate = now, CreatorUserId = Me };
                _uow.Set<SlBidDecision>().Add(row);
            }
            else
            {
                if (row.Status == SlBidStatus.Bid || row.Status == SlBidStatus.NoBid) return Bad<SlBidDecisionDto>("تصمیم گرفته شده است و چک‌لیست تغییر نمی‌کند.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlBidDecisionDto>(SlText.InvalidData);
            }
            // an edit after the submission takes the gate back to the preparer: the CEO decides on what he sees
            row.Status = SlBidStatus.Draft;
            row.SubmittedAt = null;
            row.TechnicalCapabilityOk = dto.TechnicalCapabilityOk; row.TechnicalNote = Clean(dto.TechnicalNote); row.CapacityNote = Clean(dto.CapacityNote);
            row.CommercialRiskReviewed = dto.CommercialRiskReviewed; row.CommercialRiskNote = Clean(dto.CommercialRiskNote);
            row.PaymentTermsReviewed = dto.PaymentTermsReviewed; row.PaymentTermsNote = Clean(dto.PaymentTermsNote);
            row.GuaranteesReviewed = dto.GuaranteesReviewed; row.GuaranteesNote = Clean(dto.GuaranteesNote);
            row.SanctionCheck = dto.SanctionCheck; row.SanctionNote = Clean(dto.SanctionNote); row.ExpectedMarginPercent = dto.ExpectedMarginPercent;
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            await ReadCapacityAsync(row, inquiry, ct);
            row.ModifyDate = now; row.ModifierUserId = Me;
            if (inquiry.Status == SlInquiryStatus.Received) { inquiry.Status = SlInquiryStatus.UnderReview; inquiry.ModifyDate = now; inquiry.ModifierUserId = Me; }
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlBidDecisionDto>(SlText.Concurrency);
            }
            await SlGates.BidAsync(_notify, row, inquiry.Number, inquiry.Title, ct);
            return await GetBidDecisionAsync(inquiry.Id, ct);
        }

        /// <summary>The missing items of the checklist, in Persian; empty when it is complete.</summary>
        internal static List<string> MissingChecklist(SlBidDecision d)
        {
            var missing = new List<string>();
            if (!d.TechnicalCapabilityOk.HasValue) missing.Add("توان فنی");
            if (!d.CapacityOverloadWeeks.HasValue) missing.Add("ظرفیت (تاریخ تحویل درخواستی استعلام ثبت نشده است)");
            if (!d.CommercialRiskReviewed) missing.Add("ریسک بازرگانی");
            if (!d.PaymentTermsReviewed) missing.Add("شرایط پرداخت");
            if (!d.GuaranteesReviewed) missing.Add("ضمانت‌ها");
            if (d.SanctionCheck == SlSanctionCheck.NotChecked) missing.Add("استعلام تحریم");
            return missing;
        }

        public async Task<ResponseDto<SlBidDecisionDto>> SubmitBidDecisionAsync(SlBidActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlBidDecisionDto>("چک‌لیست تصمیم‌گیری را فروش ارسال می‌کند.");
            if (dto == null) return Bad<SlBidDecisionDto>(SlText.InvalidData);
            SlBidDecision row = await _uow.Set<SlBidDecision>().FirstOrDefaultAsync(d => d.InquiryId == dto.InquiryId && !d.Deleted, ct);
            if (row == null) return SlText.NotFound<SlBidDecisionDto>("برای این استعلام هنوز چک‌لیست تصمیم‌گیری ثبت نشده است.");
            if (row.Status != SlBidStatus.Draft) return Bad<SlBidDecisionDto>("این چک‌لیست قبلاً ارسال یا تصمیم‌گیری شده است.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlBidDecisionDto>(SlText.InvalidData);
            SlInquiry inquiry = await _uow.Set<SlInquiry>().FirstAsync(i => i.Id == row.InquiryId, ct);
            // the capacity is read again: the plan changes every day
            await ReadCapacityAsync(row, inquiry, ct);
            List<string> missing = MissingChecklist(row);
            if (missing.Count > 0) return Bad<SlBidDecisionDto>("چک‌لیست کامل نیست: " + string.Join("، ", missing) + ".");
            row.Status = SlBidStatus.WaitingDecision;
            row.SubmittedAt = DateTimeOffset.Now;
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            row.ModifyDate = DateTimeOffset.Now;
            row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlBidDecisionDto>(SlText.Concurrency);
            }
            await SlGates.BidAsync(_notify, row, inquiry.Number, inquiry.Title, ct);
            return await GetBidDecisionAsync(inquiry.Id, ct);
        }

        public async Task<ResponseDto<SlBidDecisionDto>> DecideBidAsync(SlBidActionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Ceo)) return Bad<SlBidDecisionDto>("تصمیم شرکت در استعلام را فقط مدیرعامل می‌گیرد.");
            if (dto == null) return Bad<SlBidDecisionDto>(SlText.InvalidData);
            string decision = (dto.Decision ?? "").Trim().ToLowerInvariant();
            if (decision != "bid" && decision != "nobid") return Bad<SlBidDecisionDto>("تصمیم باید bid یا nobid باشد.");
            SlBidDecision row = await _uow.Set<SlBidDecision>().FirstOrDefaultAsync(d => d.InquiryId == dto.InquiryId && !d.Deleted, ct);
            if (row == null) return SlText.NotFound<SlBidDecisionDto>("برای این استعلام هنوز چک‌لیست تصمیم‌گیری ثبت نشده است.");
            if (row.Status != SlBidStatus.WaitingDecision) return Bad<SlBidDecisionDto>("چک‌لیست هنوز برای تصمیم‌گیری ارسال نشده یا قبلاً تصمیم گرفته شده است.");
            if (LedgerMath.Touched(row.CreatorUserId, row.EditorUserIds, Me)) return Bad<SlBidDecisionDto>(SlText.SameUser);
            string reason = Clean(dto.Reason);
            if (decision == "nobid" && reason == null) return Bad<SlBidDecisionDto>("برای عدم شرکت در استعلام، دلیل را بنویسید.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlBidDecisionDto>(SlText.InvalidData);
            SlInquiry inquiry = await _uow.Set<SlInquiry>().FirstAsync(i => i.Id == row.InquiryId, ct);
            DateTimeOffset now = DateTimeOffset.Now;

            row.Status = decision == "bid" ? SlBidStatus.Bid : SlBidStatus.NoBid;
            row.DecidedAt = now; row.DecidedByUserId = Me; row.DecisionReason = reason; row.ModifyDate = now; row.ModifierUserId = Me;
            Tender tender = null;
            if (decision == "bid")
            {
                inquiry.Status = SlInquiryStatus.Bidding;
                if (!inquiry.TenderId.HasValue)
                {
                    // the system drafts the tender record of the legacy tender register
                    string customerName = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => c.Id == inquiry.CustomerId).Select(c => c.Name).FirstAsync(ct);
                    tender = new Tender
                    {
                        Code = await InventoryNumbers.NextNumberAsync(_uow, SlNumbers.Tender, "TND", now.LocalDateTime, ct), PmrNo = inquiry.CustomerReference, Description = inquiry.Title,
                        Count = SlText.Number(inquiry.Quantity), Company = customerName, ReciveDate = new DateTimeOffset(inquiry.ReceivedDate, TimeZoneInfo.Local.GetUtcOffset(inquiry.ReceivedDate)),
                        TenderDate = new DateTimeOffset((inquiry.ProposalDueDate ?? inquiry.ReceivedDate).Date, TimeZoneInfo.Local.GetUtcOffset(inquiry.ReceivedDate)),
                        Result = "در حال تهیه پیشنهاد", CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                    };
                    _uow.Set<Tender>().Add(tender);
                    inquiry.TenderId = tender.Id;
                }
            }
            else
            {
                inquiry.Status = SlInquiryStatus.NoBid;
                inquiry.CancelReason = reason;
            }
            inquiry.ModifyDate = now;
            inquiry.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
                if (tender != null)
                {
                    // the key of the new tender is known only now
                    _uow.Set<SlCustomerLink>().Add(new SlCustomerLink { SourceType = SlLinkSource.Tender, SourceId = tender.Id, CustomerId = inquiry.CustomerId, Method = SlLinkMethod.FromProposal, LinkedByUserId = Me, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me });
                    await _uow.SaveChangesAsync(ct);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlBidDecisionDto>(SlText.Concurrency);
            }
            await SlGates.BidAsync(_notify, row, inquiry.Number, inquiry.Title, ct);
            return await GetBidDecisionAsync(inquiry.Id, ct);
        }
    }
}
