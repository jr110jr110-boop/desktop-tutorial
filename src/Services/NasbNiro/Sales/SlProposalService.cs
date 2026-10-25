using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Entities.Model.NasbNiro.Tenders;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Sales
{
    /// <summary>Who approves a proposal.</summary>
    internal static class SlApproval
    {
        /// <summary>
        /// Only the CEO approves when the price is below the cost of the estimate, when the CEO has not set a threshold (no number is assumed),
        /// or when the price is above the threshold. Otherwise sales approves (another person than the one who prepared it).
        /// </summary>
        public static bool NeedsCeo(decimal price, bool belowCost, decimal? threshold) => belowCost || !threshold.HasValue || price > threshold.Value;
    }

    public interface ISlProposalService
    {
        Task<ResponseDto<PagedList<SlProposalDto>>> GetProposalsAsync(SlProposalQuery query, CancellationToken ct);
        Task<ResponseDto<SlProposalDto>> GetProposalAsync(int id, CancellationToken ct);
        Task<ResponseDto<SlProposalDto>> SaveProposalAsync(SlSaveProposalDto dto, CancellationToken ct);
        Task<ResponseDto<SlProposalDto>> ProposalActionAsync(SlProposalActionDto dto, CancellationToken ct);
        Task<ResponseDto<SlProposalDto>> ResultAsync(SlProposalResultDto dto, CancellationToken ct);
        Task<ResponseDto<SlWinLossDto>> GetWinLossAsync(SlWinLossQuery query, CancellationToken ct);
    }

    /// <summary>Proposals: revisions, approval (sales up to a threshold, the CEO above it or below cost), submission, win / loss with reasons, and the contract a won proposal becomes.</summary>
    public class SlProposalService : ISlProposalService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;

        public SlProposalService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => SlText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => SlText.Bad<T>(m);

        private static bool IsActive(SlProposalStatus s) => s == SlProposalStatus.Draft || s == SlProposalStatus.Approved || s == SlProposalStatus.Submitted;

        // ================================================================================== read

        private async Task<List<SlProposalDto>> DtosAsync(List<SlProposal> rows, CancellationToken ct)
        {
            decimal? threshold = await SlSettings.GetAsync(_uow, SlSettingKeys.ProposalApprovalThreshold, ct);
            List<int> inquiryIds = rows.Select(r => r.InquiryId).Distinct().ToList();
            List<SlInquiry> inquiries = await _uow.Set<SlInquiry>().AsNoTracking().Where(i => inquiryIds.Contains(i.Id)).ToListAsync(ct);
            List<int> customerIds = inquiries.Select(i => i.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            Dictionary<int, string> users = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => r.ApprovedByUserId), ct);
            List<int> reasonIds = rows.Where(r => r.ResultReasonId.HasValue).Select(r => r.ResultReasonId.Value).Distinct().ToList();
            Dictionary<int, string> reasons = reasonIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<SlReasonCode>().AsNoTracking().Where(x => reasonIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
            List<int> contractIds = rows.Where(r => r.ContractId.HasValue).Select(r => r.ContractId.Value).Distinct().ToList();
            var reviews = contractIds.Count == 0 ? new List<(int ContractId, int Id)>() : (await _uow.Set<PcContractReview>().AsNoTracking()
                .Where(x => !x.Deleted && x.ContractId.HasValue && contractIds.Contains(x.ContractId.Value)).Select(x => new { ContractId = x.ContractId.Value, x.Id }).ToListAsync(ct))
                .Select(x => (x.ContractId, x.Id)).ToList();
            return rows.Select(r =>
            {
                SlInquiry i = inquiries.FirstOrDefault(x => x.Id == r.InquiryId);
                int? reviewId = r.ContractId.HasValue ? reviews.Where(x => x.ContractId == r.ContractId.Value).OrderByDescending(x => x.Id).Select(x => (int?)x.Id).FirstOrDefault() : null;
                return new SlProposalDto
                {
                    TouchedByMe = LedgerMath.Touched(r.CreatorUserId, r.EditorUserIds, Me),
                    Id = r.Id, Number = r.Number, Revision = r.Revision, InquiryId = r.InquiryId, InquiryNumber = i?.Number, InquiryTitle = i?.Title, CustomerId = i?.CustomerId ?? 0,
                    CustomerName = i != null && customers.TryGetValue(i.CustomerId, out string cn) ? cn : null, ProductType = i?.ProductType ?? SlProductType.Other, EstimateId = r.EstimateId,
                    TechnicalDocumentId = r.TechnicalDocumentId, CommercialDocumentId = r.CommercialDocumentId, ValidityDays = r.ValidityDays, Price = r.Price, DeliveryWeeks = r.DeliveryWeeks,
                    PaymentTermsText = r.PaymentTermsText, GuaranteesText = r.GuaranteesText, DeviationsText = r.DeviationsText, VatPercent = r.VatPercent, AdvancePercent = r.AdvancePercent,
                    RetentionPercent = r.RetentionPercent, LatePenaltyPercentPerWeek = r.LatePenaltyPercentPerWeek, PenaltyCapPercent = r.PenaltyCapPercent, ClientReviewDays = r.ClientReviewDays,
                    Status = r.Status, StatusText = SlText.ProposalStatus(r.Status), EstimateTotalCost = r.EstimateTotalCost, BelowCost = r.BelowCost, NeedsCeo = SlApproval.NeedsCeo(r.Price, r.BelowCost, threshold),
                    ApprovedAt = r.ApprovedAt, ApprovedAtText = SlText.Stamp(r.ApprovedAt), ApprovedByUserId = r.ApprovedByUserId, ApprovedByName = users.Name(r.ApprovedByUserId),
                    ApprovalNote = r.ApprovalNote, SubmittedAt = r.SubmittedAt, SubmittedAtText = SlText.Stamp(r.SubmittedAt), CustomerReference = r.CustomerReference, ResultAt = r.ResultAt,
                    ResultAtText = SlText.Stamp(r.ResultAt), ResultReasonId = r.ResultReasonId, ResultReason = r.ResultReasonId.HasValue && reasons.TryGetValue(r.ResultReasonId.Value, out string rt) ? rt : null,
                    Competitor = r.Competitor, CompetitorPrice = r.CompetitorPrice, LessonsLearned = r.LessonsLearned, ContractId = r.ContractId, ContractReviewId = reviewId, RowVersion = SlConcurrency.Text(r.RowVersion),
                };
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SlProposalDto>>> GetProposalsAsync(SlProposalQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.CommercialReaders)) return Bad<PagedList<SlProposalDto>>(SlText.NotAllowed);
            query ??= new SlProposalQuery();
            IQueryable<SlProposal> q = _uow.Set<SlProposal>().AsNoTracking().Where(p => !p.Deleted);
            if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
            if (query.InquiryId.HasValue) q = q.Where(p => p.InquiryId == query.InquiryId.Value);
            if (query.CustomerId.HasValue) { int cid = query.CustomerId.Value; q = q.Where(p => _uow.Set<SlInquiry>().Any(i => i.Id == p.InquiryId && i.CustomerId == cid)); }
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(p => p.Number.Contains(text));
            return ResponseDtoHelper.Ok(await SlText.CutAsync(q.OrderByDescending(p => p.Id), query.PageNumber, query.PageSize, rows => DtosAsync(rows, ct), ct));
        }

        public async Task<ResponseDto<SlProposalDto>> GetProposalAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.CommercialReaders)) return Bad<SlProposalDto>(SlText.NotAllowed);
            SlProposal row = await _uow.Set<SlProposal>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && !p.Deleted, ct);
            if (row == null) return SlText.NotFound<SlProposalDto>("پیشنهاد پیدا نشد.");
            return ResponseDtoHelper.Ok((await DtosAsync(new List<SlProposal> { row }, ct))[0]);
        }

        // ================================================================================== save

        private static string ValidateTerms(SlSaveProposalDto d)
        {
            foreach (decimal? v in new[] { d.VatPercent, d.AdvancePercent, d.RetentionPercent, d.LatePenaltyPercentPerWeek, d.PenaltyCapPercent })
                if (v.HasValue && (v.Value < 0 || v.Value > 100)) return "درصدهای شرایط قرارداد باید بین ۰ و ۱۰۰ باشد.";
            if (d.ValidityDays.HasValue && d.ValidityDays.Value <= 0) return "مدت اعتبار پیشنهاد باید بیشتر از صفر باشد.";
            if (d.DeliveryWeeks.HasValue && d.DeliveryWeeks.Value <= 0) return "مدت تحویل (هفته) باید بیشتر از صفر باشد.";
            if (d.ClientReviewDays.HasValue && d.ClientReviewDays.Value <= 0) return "مهلت بررسی کارفرما باید بیشتر از صفر باشد.";
            return null;
        }

        public async Task<ResponseDto<SlProposalDto>> SaveProposalAsync(SlSaveProposalDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlProposalDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlProposalDto>(SlText.InvalidData);
            string problem = ValidateTerms(dto);
            if (problem != null) return Bad<SlProposalDto>(problem);
            SlInquiry inquiry = await _uow.Set<SlInquiry>().FirstOrDefaultAsync(i => i.Id == dto.InquiryId && !i.Deleted, ct);
            if (inquiry == null) return SlText.NotFound<SlProposalDto>("استعلام پیدا نشد.");
            if (inquiry.Status != SlInquiryStatus.Bidding) return Bad<SlProposalDto>($"پیشنهاد فقط برای استعلامی ثبت می‌شود که در حال تهیه پیشنهاد است؛ وضعیت فعلی: {SlText.InquiryStatus(inquiry.Status)}.");
            SlEstimate estimate = await _uow.Set<SlEstimate>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EstimateId && !e.Deleted, ct);
            if (estimate == null || estimate.InquiryId != inquiry.Id) return Bad<SlProposalDto>("برآورد پیدا نشد یا مال این استعلام نیست.");
            if (estimate.Status != SlEstimateStatus.Final || !estimate.Price.HasValue) return Bad<SlProposalDto>("پیشنهاد فقط از برآورد نهایی‌شده ساخته می‌شود.");
            decimal price = dto.Price ?? estimate.Price.Value;
            if (price <= 0) return Bad<SlProposalDto>("قیمت پیشنهاد باید بیشتر از صفر باشد.");

            DateTimeOffset now = DateTimeOffset.Now;
            SlProposal row;
            if (dto.Id.HasValue)
            {
                row = await _uow.Set<SlProposal>().FirstOrDefaultAsync(p => p.Id == dto.Id.Value && !p.Deleted, ct);
                if (row == null) return SlText.NotFound<SlProposalDto>("پیشنهاد پیدا نشد.");
                if (row.InquiryId != inquiry.Id) return Bad<SlProposalDto>("استعلام پیشنهاد تغییر نمی‌کند.");
                if (row.Status != SlProposalStatus.Draft) return Bad<SlProposalDto>("فقط پیشنهاد پیش‌نویس ویرایش می‌شود؛ برای تغییر پیشنهاد ارسال‌شده بازنگری بسازید.");
                if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlProposalDto>(SlText.InvalidData);
            }
            else
            {
                if (await _uow.Set<SlProposal>().AnyAsync(p => p.InquiryId == inquiry.Id && !p.Deleted && (p.Status == SlProposalStatus.Draft || p.Status == SlProposalStatus.Approved || p.Status == SlProposalStatus.Submitted), ct))
                    return Bad<SlProposalDto>("این استعلام پیشنهاد فعال دارد؛ آن را ویرایش یا لغو کنید، یا بازنگری بسازید.");
                row = new SlProposal { InquiryId = inquiry.Id, Revision = 1, CreateDate = now, CreatorUserId = Me };
                row.Number = await InventoryNumbers.NextNumberAsync(_uow, SlNumbers.Proposal, "PRP", now.LocalDateTime, ct);
                _uow.Set<SlProposal>().Add(row);
            }
            row.EstimateId = estimate.Id; row.TechnicalDocumentId = dto.TechnicalDocumentId; row.CommercialDocumentId = dto.CommercialDocumentId; row.ValidityDays = dto.ValidityDays; row.Price = price;
            row.DeliveryWeeks = dto.DeliveryWeeks; row.PaymentTermsText = Clean(dto.PaymentTermsText); row.GuaranteesText = Clean(dto.GuaranteesText); row.DeviationsText = Clean(dto.DeviationsText);
            row.VatPercent = dto.VatPercent; row.AdvancePercent = dto.AdvancePercent; row.RetentionPercent = dto.RetentionPercent; row.LatePenaltyPercentPerWeek = dto.LatePenaltyPercentPerWeek;
            row.PenaltyCapPercent = dto.PenaltyCapPercent; row.ClientReviewDays = dto.ClientReviewDays; row.CustomerReference = Clean(dto.CustomerReference);
            row.EstimateTotalCost = estimate.TotalCost;
            row.BelowCost = price < estimate.TotalCost;
            // those who prepared the numbers behind the price cannot approve the proposal either
            row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, Me);
            foreach (string id in (estimate.EditorUserIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(id, out int editor)) row.EditorUserIds = LedgerMath.WithEditor(row.EditorUserIds, editor);
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlProposalDto>(SlText.Concurrency);
            }
            await SyncGatesAsync(row, ct);
            return await GetProposalAsync(row.Id, ct);
        }

        private async Task SyncGatesAsync(SlProposal row, CancellationToken ct)
        {
            decimal? threshold = await SlSettings.GetAsync(_uow, SlSettingKeys.ProposalApprovalThreshold, ct);
            await SlGates.ProposalAsync(_notify, row, SlApproval.NeedsCeo(row.Price, row.BelowCost, threshold), ct);
            await SlGates.BelowCostAsync(_notify, row, ct);
        }

        // ================================================================================== actions

        public async Task<ResponseDto<SlProposalDto>> ProposalActionAsync(SlProposalActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<SlProposalDto>(SlText.InvalidData);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            if (action != "approve" && action != "submit" && action != "revise" && action != "cancel") return Bad<SlProposalDto>("عمل نامعتبر است (approve، submit، revise یا cancel).");
            SlProposal row = await _uow.Set<SlProposal>().FirstOrDefaultAsync(p => p.Id == dto.Id && !p.Deleted, ct);
            if (row == null) return SlText.NotFound<SlProposalDto>("پیشنهاد پیدا نشد.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlProposalDto>(SlText.InvalidData);
            SlInquiry inquiry = await _uow.Set<SlInquiry>().FirstAsync(i => i.Id == row.InquiryId, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            string note = Clean(dto.Note);
            SlProposal result = row;

            if (action == "approve")
            {
                if (!_user.IsInRole(SalesRoles.ProposalApprovers)) return Bad<SlProposalDto>(SlText.NotAllowed);
                if (row.Status != SlProposalStatus.Draft) return Bad<SlProposalDto>("فقط پیشنهاد پیش‌نویس تأیید می‌شود.");
                SlEstimate estimate = await _uow.Set<SlEstimate>().AsNoTracking().FirstAsync(e => e.Id == row.EstimateId, ct);
                if (estimate.Status != SlEstimateStatus.Final) return Bad<SlProposalDto>("برآورد این پیشنهاد بازنگری شده است؛ پیشنهاد را با برآورد جدید ذخیره کنید.");
                row.BelowCost = row.Price < estimate.TotalCost;
                row.EstimateTotalCost = estimate.TotalCost;
                decimal? threshold = await SlSettings.GetAsync(_uow, SlSettingKeys.ProposalApprovalThreshold, ct);
                bool needsCeo = SlApproval.NeedsCeo(row.Price, row.BelowCost, threshold);
                if (needsCeo && !_user.IsInRole(SalesRoles.Ceo))
                    return Bad<SlProposalDto>(row.BelowCost ? "قیمت پیشنهاد کمتر از بهای تمام‌شده است؛ فقط مدیرعامل تأیید می‌کند."
                        : !threshold.HasValue ? "سقف تأیید فروش را مدیرعامل هنوز تعیین نکرده است؛ تا آن زمان تأیید پیشنهاد با مدیرعامل است."
                        : $"قیمت پیشنهاد از سقف تأیید فروش ({SlText.Money(threshold.Value)} ریال) بیشتر است؛ تأیید با مدیرعامل است.");
                if (LedgerMath.Touched(row.CreatorUserId, row.EditorUserIds, Me)) return Bad<SlProposalDto>(SlText.SameUser);
                if (row.BelowCost && note == null) return Bad<SlProposalDto>("برای تأیید پیشنهاد زیر بهای تمام‌شده، دلیل را در توضیح بنویسید.");
                row.Status = SlProposalStatus.Approved; row.ApprovedAt = now; row.ApprovedByUserId = Me; row.ApprovalNote = note;
            }
            else if (action == "submit")
            {
                if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlProposalDto>(SlText.NotAllowed);
                if (row.Status != SlProposalStatus.Approved) return Bad<SlProposalDto>("فقط پیشنهاد تأییدشده برای مشتری ارسال می‌شود.");
                row.Status = SlProposalStatus.Submitted; row.SubmittedAt = now; row.SubmittedByUserId = Me;
                inquiry.Status = SlInquiryStatus.Submitted; inquiry.ModifyDate = now; inquiry.ModifierUserId = Me;
                await TenderAsync(inquiry, t => { t.TechSendDate = now; t.FinanceSendDate = now; t.Result = "پیشنهاد ارسال شد (" + row.Number + " بازنگری " + row.Revision + ")"; }, ct);
            }
            else if (action == "revise")
            {
                if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlProposalDto>(SlText.NotAllowed);
                if (row.Status != SlProposalStatus.Submitted && row.Status != SlProposalStatus.Approved) return Bad<SlProposalDto>("بازنگری فقط از پیشنهاد تأییدشده یا ارسال‌شده ساخته می‌شود.");
                int revision = await _uow.Set<SlProposal>().Where(p => p.Number == row.Number).MaxAsync(p => p.Revision, ct) + 1;
                // maker-checker: the revision carries everyone who touched the proposal and the estimate behind it
                string estimateEditors = await _uow.Set<SlEstimate>().AsNoTracking().Where(e => e.Id == row.EstimateId).Select(e => e.EditorUserIds).FirstOrDefaultAsync(ct);
                string editors = LedgerMath.WithEditor(row.EditorUserIds, row.CreatorUserId);
                foreach (string id in (estimateEditors ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(id, out int editor)) editors = LedgerMath.WithEditor(editors, editor);
                SlProposal copy = new SlProposal
                {
                    Number = row.Number, Revision = revision, InquiryId = row.InquiryId, EstimateId = row.EstimateId, TechnicalDocumentId = row.TechnicalDocumentId, CommercialDocumentId = row.CommercialDocumentId,
                    ValidityDays = row.ValidityDays, Price = row.Price, DeliveryWeeks = row.DeliveryWeeks, PaymentTermsText = row.PaymentTermsText, GuaranteesText = row.GuaranteesText,
                    DeviationsText = row.DeviationsText, VatPercent = row.VatPercent, AdvancePercent = row.AdvancePercent, RetentionPercent = row.RetentionPercent,
                    LatePenaltyPercentPerWeek = row.LatePenaltyPercentPerWeek, PenaltyCapPercent = row.PenaltyCapPercent, ClientReviewDays = row.ClientReviewDays, Status = SlProposalStatus.Draft,
                    EstimateTotalCost = row.EstimateTotalCost, BelowCost = row.BelowCost, CustomerReference = row.CustomerReference, EditorUserIds = LedgerMath.WithEditor(editors, Me),
                    CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
                };
                _uow.Set<SlProposal>().Add(copy);
                row.Status = SlProposalStatus.Revised;
                if (inquiry.Status == SlInquiryStatus.Submitted) { inquiry.Status = SlInquiryStatus.Bidding; inquiry.ModifyDate = now; inquiry.ModifierUserId = Me; }
                result = copy;
            }
            else
            {
                if (!_user.IsInRole(SalesRoles.Writers)) return Bad<SlProposalDto>(SlText.NotAllowed);
                if (!IsActive(row.Status)) return Bad<SlProposalDto>($"پیشنهاد در وضعیت «{SlText.ProposalStatus(row.Status)}» لغو نمی‌شود.");
                if (note == null) return Bad<SlProposalDto>("دلیل لغو پیشنهاد را بنویسید.");
                if (row.Status == SlProposalStatus.Submitted && inquiry.Status == SlInquiryStatus.Submitted) { inquiry.Status = SlInquiryStatus.Bidding; inquiry.ModifyDate = now; inquiry.ModifierUserId = Me; }
                row.Status = SlProposalStatus.Cancelled; row.ApprovalNote = note;
            }
            row.ModifyDate = now; row.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlProposalDto>(SlText.Concurrency);
            }
            await SyncGatesAsync(row, ct);
            if (!ReferenceEquals(result, row)) await SyncGatesAsync(result, ct);
            return await GetProposalAsync(result.Id, ct);
        }

        private async Task TenderAsync(SlInquiry inquiry, Action<Tender> change, CancellationToken ct)
        {
            if (!inquiry.TenderId.HasValue) return;
            Tender tender = await _uow.Set<Tender>().FirstOrDefaultAsync(t => t.Id == inquiry.TenderId.Value && !t.Deleted, ct);
            if (tender == null) return;
            change(tender);
            tender.ModifyDate = DateTimeOffset.Now;
            tender.ModifierUserId = Me;
        }

        // ================================================================================== result and contract

        public async Task<ResponseDto<SlProposalDto>> ResultAsync(SlProposalResultDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.ProposalApprovers)) return Bad<SlProposalDto>(SlText.NotAllowed);
            if (dto == null) return Bad<SlProposalDto>(SlText.InvalidData);
            string result = (dto.Result ?? "").Trim().ToLowerInvariant();
            if (result != "won" && result != "lost") return Bad<SlProposalDto>("نتیجه باید won یا lost باشد.");
            bool won = result == "won";
            if (dto.CompetitorPrice.HasValue && dto.CompetitorPrice.Value < 0) return Bad<SlProposalDto>("قیمت رقیب نمی‌تواند منفی باشد.");
            SlProposal row = await _uow.Set<SlProposal>().FirstOrDefaultAsync(p => p.Id == dto.Id && !p.Deleted, ct);
            if (row == null) return SlText.NotFound<SlProposalDto>("پیشنهاد پیدا نشد.");
            if (row.Status != SlProposalStatus.Submitted) return Bad<SlProposalDto>("نتیجه فقط برای پیشنهاد ارسال‌شده ثبت می‌شود.");
            if (!SlConcurrency.Apply(_uow, row, dto.RowVersion)) return Bad<SlProposalDto>(SlText.InvalidData);
            if (!won && !dto.ReasonId.HasValue) return Bad<SlProposalDto>("برای پیشنهاد بازنده، کد دلیل را انتخاب کنید.");
            if (dto.ReasonId.HasValue)
            {
                SlReasonKind kind = won ? SlReasonKind.Won : SlReasonKind.Lost;
                if (!await _uow.Set<SlReasonCode>().AnyAsync(r => r.Id == dto.ReasonId.Value && r.Kind == kind && r.IsActive && !r.Deleted, ct))
                    return Bad<SlProposalDto>(won ? "کد دلیل برنده‌شدن پیدا نشد یا غیرفعال است." : "کد دلیل بازنده‌شدن پیدا نشد یا غیرفعال است.");
            }
            if (won && !_user.IsInRole(SalesRoles.ContractMakers)) return Bad<SlProposalDto>(SlText.NotAllowed);
            SlInquiry inquiry = await _uow.Set<SlInquiry>().FirstAsync(i => i.Id == row.InquiryId, ct);
            if (won && !await _uow.Set<FnCustomer>().AnyAsync(c => c.Id == inquiry.CustomerId, ct)) return Bad<SlProposalDto>("مشتری پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            row.Status = won ? SlProposalStatus.Won : SlProposalStatus.Lost;
            row.ResultAt = now; row.ResultReasonId = dto.ReasonId; row.Competitor = Clean(dto.Competitor); row.CompetitorPrice = dto.CompetitorPrice; row.LessonsLearned = Clean(dto.LessonsLearned);
            row.ModifyDate = now; row.ModifierUserId = Me;
            inquiry.Status = won ? SlInquiryStatus.Won : SlInquiryStatus.Lost; inquiry.ModifyDate = now; inquiry.ModifierUserId = Me;
            await TenderAsync(inquiry, t => t.Result = won ? "برنده" : "بازنده", ct);
            ContractEntity contract = null;
            if (won)
            {
                contract = await DraftContractAsync(row, inquiry, now, ct);
                row.Contract = contract;
                // the link of the new contract to the customer saves in the same unit of work
                _uow.Set<SlCustomerLink>().Add(new SlCustomerLink { Contract = contract, CustomerId = inquiry.CustomerId, Method = SlLinkMethod.FromProposal, LinkedByUserId = Me, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me });
            }
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<SlProposalDto>(SlText.Concurrency);
            }
            await SyncGatesAsync(row, ct);
            return await GetProposalAsync(row.Id, ct);
        }

        /// <summary>
        /// The system drafts everything a won proposal starts: the contract, its terms (rial), the contract equipment, the contract review (pending, for a person to decide)
        /// and the project initiation. Nothing is saved here.
        /// </summary>
        private async Task<ContractEntity> DraftContractAsync(SlProposal p, SlInquiry inquiry, DateTimeOffset now, CancellationToken ct)
        {
            FnCustomer customer = await _uow.Set<FnCustomer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == inquiry.CustomerId, ct);
            if (customer == null) return null;
            Tender tender = inquiry.TenderId.HasValue ? await _uow.Set<Tender>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == inquiry.TenderId.Value, ct) : null;
            int days = (p.DeliveryWeeks ?? 0) * 7;
            DateTimeOffset delivery = now.AddDays(days);
            var contract = new ContractEntity
            {
                Code = await InventoryNumbers.NextNumberAsync(_uow, SlNumbers.Contract, "CT", now.LocalDateTime, ct), Title = inquiry.Title, TenderCode = tender?.Code,
                EmployerOrderNo = p.CustomerReference ?? inquiry.CustomerReference, Employer = Clean(customer.EmployerName) ?? customer.Name, ContractDate = now, AnnounceDate = now, StartDate = now,
                PayDate = now, ActualStartDate = now, TempDate = delivery, DeliverDate = delivery, GurantyDate = delivery, EndDate = delivery, Price = p.Price, Duration = p.DeliveryWeeks.HasValue ? days : (int?)null,
                Status = ContractStatus.Current, Services = SlText.Product(inquiry.ProductType), CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            };
            _uow.Set<ContractEntity>().Add(contract);
            _uow.Set<PcContractTerms>().Add(new PcContractTerms
            {
                Contract = contract, Category = SlText.Category(inquiry.ProductType), CurrencyCode = "IRR", ValueExVat = p.Price, VatPercent = p.VatPercent, AdvancePercent = p.AdvancePercent,
                RetentionPercent = p.RetentionPercent, LatePenaltyPercentPerWeek = p.LatePenaltyPercentPerWeek, PenaltyCapPercent = p.PenaltyCapPercent,
                ClientReviewDays = p.ClientReviewDays ?? 14, ContractDeliveryDate = p.DeliveryWeeks.HasValue ? delivery.Date : (DateTime?)null, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            });
            List<SlEstimateEquipment> equipments = await _uow.Set<SlEstimateEquipment>().AsNoTracking().Where(e => e.EstimateId == p.EstimateId).OrderBy(e => e.Id).ToListAsync(ct);
            foreach (SlEstimateEquipment e in equipments)
                _uow.Set<ContractEquipment>().Add(new ContractEquipment { Contract = contract, EquipmentId = e.EquipmentId, Count = e.Count });
            _uow.Set<PcContractReview>().Add(new PcContractReview
            {
                Contract = contract, TenderCode = tender?.Code, Kind = SlText.ReviewKind(inquiry.ProductType), Decision = PcReviewDecision.Pending,
                Notes = $"پیش‌نویس سیستمی از پیشنهاد برنده {p.Number} (بازنگری {p.Revision}) و استعلام {inquiry.Number}؛ بازنگری قرارداد را افراد مسئول تکمیل و تصمیم‌گیری می‌کنند.",
                CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me,
            });
            await PcProjectService.DraftInitiationAsync(_uow, contract, 0, Me, ct);
            return contract;
        }

        // ================================================================================== statistics

        public async Task<ResponseDto<SlWinLossDto>> GetWinLossAsync(SlWinLossQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(SalesRoles.CommercialReaders)) return Bad<SlWinLossDto>(SlText.NotAllowed);
            query ??= new SlWinLossQuery();
            IQueryable<SlProposal> q = _uow.Set<SlProposal>().AsNoTracking().Where(p => !p.Deleted && (p.Status == SlProposalStatus.Won || p.Status == SlProposalStatus.Lost));
            if (query.From.HasValue) { DateTimeOffset from = new DateTimeOffset(query.From.Value.Date, TimeZoneInfo.Local.GetUtcOffset(query.From.Value.Date)); q = q.Where(p => p.ResultAt >= from); }
            if (query.To.HasValue) { DateTime day = query.To.Value.Date.AddDays(1); DateTimeOffset to = new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day)); q = q.Where(p => p.ResultAt < to); }
            var rows = await q.Select(p => new { p.Status, p.Price, p.InquiryId, p.ResultReasonId }).ToListAsync(ct);
            List<int> inquiryIds = rows.Select(r => r.InquiryId).Distinct().ToList();
            var inquiries = (await _uow.Set<SlInquiry>().AsNoTracking().Where(i => inquiryIds.Contains(i.Id)).Select(i => new { i.Id, i.ProductType, i.CustomerId }).ToListAsync(ct)).ToDictionary(i => i.Id);
            List<int> customerIds = inquiries.Values.Select(i => i.CustomerId).Distinct().ToList();
            Dictionary<int, string> customers = await _uow.Set<FnCustomer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            List<int> reasonIds = rows.Where(r => r.ResultReasonId.HasValue).Select(r => r.ResultReasonId.Value).Distinct().ToList();
            Dictionary<int, string> reasons = reasonIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<SlReasonCode>().AsNoTracking().Where(r => reasonIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Title, ct);

            int won = rows.Count(r => r.Status == SlProposalStatus.Won), lost = rows.Count - won;
            var dto = new SlWinLossDto { Won = won, Lost = lost, HitRatePercent = Percent(won, lost) };
            var byProduct = new Dictionary<int, SlWinLossRowDto>();
            var byCustomer = new Dictionary<int, SlWinLossRowDto>();
            var byReason = new Dictionary<int, SlWinLossRowDto>();
            foreach (var r in rows)
            {
                var inq = inquiries[r.InquiryId];
                Add(byProduct, (int)inq.ProductType, SlText.Product(inq.ProductType), r.Status, r.Price);
                Add(byCustomer, inq.CustomerId, customers.TryGetValue(inq.CustomerId, out string cn) ? cn : null, r.Status, r.Price);
                if (r.ResultReasonId.HasValue) Add(byReason, r.ResultReasonId.Value, reasons.TryGetValue(r.ResultReasonId.Value, out string rn) ? rn : null, r.Status, r.Price);
            }
            dto.ByProduct = byProduct.Values.OrderBy(g => g.Key).ToList();
            dto.ByCustomer = byCustomer.Values.OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.Key).ToList();
            dto.ByReason = byReason.Values.OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.Key).ToList();
            return ResponseDtoHelper.Ok(dto);
        }

        private static void Add(Dictionary<int, SlWinLossRowDto> map, int key, string name, SlProposalStatus status, decimal price)
        {
            if (!map.TryGetValue(key, out SlWinLossRowDto g)) map[key] = g = new SlWinLossRowDto { Key = key, Name = name };
            if (status == SlProposalStatus.Won) { g.Won++; g.WonValue += price; } else { g.Lost++; g.LostValue += price; }
            g.HitRatePercent = Percent(g.Won, g.Lost);
        }

        internal static decimal? Percent(int won, int lost) => won + lost == 0 ? (decimal?)null : Math.Round(won * 100m / (won + lost), 2, MidpointRounding.AwayFromZero);
    }
}
