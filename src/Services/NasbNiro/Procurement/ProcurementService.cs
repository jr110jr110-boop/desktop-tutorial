using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Procurement
{
    public interface IProcurementService
    {
        Task<ResponseDto<List<ProcurementSettingDto>>> GetSettingsAsync(CancellationToken cancellationToken);
        Task<ResponseDto<ProcurementSettingDto>> SaveSettingAsync(ProcurementSettingDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PurchaseRequestDto>>> GetRequestsAsync(PurchaseRequestQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PurchaseRequestDto>> GetRequestAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PurchaseRequestDto>> SaveRequestAsync(SavePurchaseRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PurchaseRequestDto>> RequestActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<InquiryDto>>> GetInquiriesAsync(int? purchaseRequestId, CancellationToken cancellationToken);
        Task<ResponseDto<InquiryDto>> GetInquiryAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<InquiryDto>> SaveInquiryAsync(SaveInquiryDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<InquiryDto>> SelectQuoteAsync(SelectQuoteDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<InquiryDto>> InquiryActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PurchaseOrderDto>>> GetOrdersAsync(PurchaseOrderQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PurchaseOrderDto>> GetOrderAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PurchaseOrderDto>> SaveOrderAsync(SavePurchaseOrderDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PurchaseOrderDto>> CreateOrderFromInquiryAsync(int inquiryId, CancellationToken cancellationToken);
        Task<ResponseDto<PurchaseOrderDto>> OrderActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PurchaseOrderDto>> AcceptServiceAsync(SaveServiceAcceptanceDto dto, CancellationToken cancellationToken);

        /// <summary>Quantity accepted per order line (QC-accepted receipts, receipts without inspection, accepted services).</summary>
        Task<Dictionary<int, decimal>> AcceptedQuantitiesAsync(IEnumerable<int> orderLineIds, CancellationToken cancellationToken);

        /// <summary>Called by the warehouse after a receipt or a return of an order is confirmed or cancelled.</summary>
        Task RefreshOrderAsync(int purchaseOrderId, CancellationToken cancellationToken);
    }

    public class ProcurementService : IProcurementService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;

        public ProcurementService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "این تایید باید توسط شخص دیگری غیر از ثبت‌کننده یا تاییدکننده قبلی انجام شود.";

        // ------------------------------------------------------------------------------- settings

        public async Task<ResponseDto<List<ProcurementSettingDto>>> GetSettingsAsync(CancellationToken cancellationToken)
        {
            await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            List<ProcurementSettingDto> list = await _uow.Set<ProcurementSetting>().OrderBy(s => s.Id)
                .Select(s => new ProcurementSettingDto { Key = s.Key, Value = s.Value, Title = s.Title, Source = s.Source }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<ProcurementSettingDto>> SaveSettingAsync(ProcurementSettingDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.Management)) return Bad<ProcurementSettingDto>(NotAllowed);
            await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            ProcurementSetting setting = await _uow.Set<ProcurementSetting>().FirstOrDefaultAsync(s => s.Key == dto.Key, cancellationToken);
            if (setting == null) return ResponseDtoHelper.NotFound<ProcurementSettingDto>("تنظیم پیدا نشد.");
            if (dto.Value < 0) return Bad<ProcurementSettingDto>("مقدار نمی‌تواند منفی باشد.");
            setting.Value = dto.Value;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new ProcurementSettingDto { Key = setting.Key, Value = setting.Value, Title = setting.Title, Source = setting.Source });
        }

        // ------------------------------------------------------------------------ purchase requests

        private IQueryable<PurchaseRequestDto> RequestProjection(IQueryable<PurchaseRequest> source) =>
            source.Select(r => new PurchaseRequestDto
            {
                Id = r.Id, Number = r.Number, Source = r.Source, Kind = r.Kind, Status = r.Status, RequestDate = r.RequestDate,
                WorkOrderId = r.WorkOrderId, WorkOrderCode = r.WorkOrder.Code, ContractId = r.ContractId, ContractCode = r.Contract.Code,
                CostCenter = r.CostCenter, MaterialRequestId = r.MaterialRequestId, Description = r.Description, EstimatedAmount = r.EstimatedAmount,
                BoardResolution = r.BoardResolution, CreateDate = r.CreateDate, CreatorUserId = r.CreatorUserId, SubmittedAt = r.SubmittedAt,
                PlanningApprovedAt = r.PlanningApprovedAt, PlanningApprovedByUserId = r.PlanningApprovedByUserId,
                ManagementApprovedAt = r.ManagementApprovedAt, ManagementApprovedByUserId = r.ManagementApprovedByUserId,
                RejectedAt = r.RejectedAt, RejectReason = r.RejectReason,
                Lines = r.Lines.OrderBy(l => l.RowNumber).Select(l => new PurchaseRequestLineDto
                {
                    Id = l.Id, RowNumber = l.RowNumber, ItemId = l.ItemId, ItemCode = l.Item.Code, ItemTitle = l.Item.Title,
                    Description = l.Description, Unit = l.Unit, Quantity = l.Quantity, NeededBy = l.NeededBy,
                    EstimatedUnitPrice = l.EstimatedUnitPrice, TechnicalRequirements = l.TechnicalRequirements, BuyOrderId = l.BuyOrderId,
                }).ToList(),
            });

        private async Task FillRequestsAsync(List<PurchaseRequestDto> rows, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow,
                rows.SelectMany(r => new int?[] { r.CreatorUserId, r.PlanningApprovedByUserId, r.ManagementApprovedByUserId }), cancellationToken);
            List<int> lineIds = rows.SelectMany(r => r.Lines.Select(l => l.Id)).ToList();
            var ordered = await _uow.Set<PurchaseOrderLine>()
                .Where(l => l.RequestLineId.HasValue && lineIds.Contains(l.RequestLineId.Value) && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled && !l.PurchaseOrder.Deleted)
                .GroupBy(l => l.RequestLineId.Value).Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) }).ToListAsync(cancellationToken);
            foreach (PurchaseRequestDto r in rows)
            {
                r.CreatorName = names.Name(r.CreatorUserId);
                r.PlanningApprovedByName = names.Name(r.PlanningApprovedByUserId);
                r.ManagementApprovedByName = names.Name(r.ManagementApprovedByUserId);
                r.RequiredQuotes = ProcurementSettingKeys.RequiredQuotes(s, r.EstimatedAmount);
                r.NeedsManagement = r.EstimatedAmount >= s[ProcurementSettingKeys.MediumFrom];
                r.NeedsBoard = r.EstimatedAmount >= s[ProcurementSettingKeys.MajorFrom];
                foreach (PurchaseRequestLineDto l in r.Lines) l.OrderedQuantity = ordered.FirstOrDefault(o => o.Key == l.Id)?.Quantity ?? 0;
            }
        }

        public async Task<ResponseDto<PagedList<PurchaseRequestDto>>> GetRequestsAsync(PurchaseRequestQuery query, CancellationToken cancellationToken)
        {
            query ??= new PurchaseRequestQuery();
            IQueryable<PurchaseRequest> requests = _uow.Set<PurchaseRequest>().Where(r => !r.Deleted);
            if (query.Status.HasValue) requests = requests.Where(r => r.Status == query.Status.Value);
            if (query.WorkOrderId.HasValue) requests = requests.Where(r => r.WorkOrderId == query.WorkOrderId.Value);
            string text = Clean(query.Contains);
            if (text != null)
                requests = requests.Where(r => r.Number.Contains(text) || r.Description.Contains(text) ||
                                               r.Lines.Any(l => l.Item.Code.Contains(text) || l.Item.Title.Contains(text) || l.Description.Contains(text)));
            PagedList<PurchaseRequestDto> page = await PagedList<PurchaseRequestDto>.CreateAsync(
                RequestProjection(requests.OrderByDescending(r => r.Id)), query.PageNumber, query.PageSize, cancellationToken);
            await FillRequestsAsync(page, cancellationToken);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<PurchaseRequestDto>> GetRequestAsync(int id, CancellationToken cancellationToken)
        {
            List<PurchaseRequestDto> list = await RequestProjection(_uow.Set<PurchaseRequest>().Where(r => r.Id == id && !r.Deleted)).ToListAsync(cancellationToken);
            if (list.Count == 0) return ResponseDtoHelper.NotFound<PurchaseRequestDto>("درخواست خرید پیدا نشد.");
            await FillRequestsAsync(list, cancellationToken);
            return ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<PurchaseRequestDto>> SaveRequestAsync(SavePurchaseRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PurchaseRequestDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!Enum.IsDefined(typeof(PurchaseRequestSource), dto.Source) || !Enum.IsDefined(typeof(PurchaseKind), dto.Kind))
                return Bad<PurchaseRequestDto>("نوع یا منبع درخواست نامعتبر است.");
            if (dto.RequestDate == default) dto.RequestDate = DateTime.Today;
            if (dto.Source == PurchaseRequestSource.Bom && !_user.IsInRole(ProcurementRoles.Planning))
                return Bad<PurchaseRequestDto>("درخواست خرید BOM را واحد برنامه‌ریزی و کنترل پروژه ثبت می‌کند.");
            if (dto.Source == PurchaseRequestSource.Bom && !dto.WorkOrderId.HasValue && !dto.ContractId.HasValue)
                return Bad<PurchaseRequestDto>("درخواست BOM باید پروژه یا دستور ساخت داشته باشد.");
            if (dto.WorkOrderId.HasValue)
            {
                int? contract = await _uow.Set<WorkOrder>().Where(w => w.Id == dto.WorkOrderId.Value && !w.Deleted).Select(w => (int?)w.ContractId).FirstOrDefaultAsync(cancellationToken);
                if (contract == null) return Bad<PurchaseRequestDto>("دستور ساخت پیدا نشد.");
                dto.ContractId ??= contract;
            }
            if (dto.ContractId.HasValue && !await _uow.Set<ContractEntity>().AnyAsync(c => c.Id == dto.ContractId.Value && !c.Deleted, cancellationToken))
                return Bad<PurchaseRequestDto>("قرارداد پیدا نشد.");

            List<SavePurchaseRequestLineDto> lines = (dto.Lines ?? new List<SavePurchaseRequestLineDto>()).Where(l => l != null).ToList();
            if (lines.Count == 0) return Bad<PurchaseRequestDto>("درخواست حداقل یک ردیف لازم دارد.");
            List<int> itemIds = lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().Where(i => itemIds.Contains(i.Id) && !i.Deleted).ToDictionaryAsync(i => i.Id, cancellationToken);
            Dictionary<int, decimal> costs = await _uow.Set<ItemCost>().Where(c => itemIds.Contains(c.ItemId))
                .ToDictionaryAsync(c => c.ItemId, c => c.LastPurchasePrice ?? c.AverageCost, cancellationToken);
            for (int i = 0; i < lines.Count; i++)
            {
                SavePurchaseRequestLineDto l = lines[i];
                string row = $"ردیف {i + 1}: ";
                if (l.Quantity <= 0) return Bad<PurchaseRequestDto>(row + "مقدار باید بیشتر از صفر باشد.");
                if (l.ItemId.HasValue)
                {
                    if (!items.TryGetValue(l.ItemId.Value, out InventoryItem item)) return Bad<PurchaseRequestDto>(row + "کالا پیدا نشد.");
                    if (!item.IsActive) return Bad<PurchaseRequestDto>(row + $"کالای {item.Code} غیرفعال است.");
                    l.Unit ??= item.Unit;
                    if (!l.EstimatedUnitPrice.HasValue && costs.TryGetValue(item.Id, out decimal c) && c > 0) l.EstimatedUnitPrice = c;
                }
                else
                {
                    if (dto.Kind == PurchaseKind.Goods) return Bad<PurchaseRequestDto>(row + "کالای خریدنی باید کد کالا داشته باشد (WI-WH-002)؛ برای کالای جدید «درخواست کد کالا» بزنید.");
                    if (Clean(l.Description) == null) return Bad<PurchaseRequestDto>(row + "شرح خدمت را بنویسید.");
                }
                if (l.EstimatedUnitPrice < 0) return Bad<PurchaseRequestDto>(row + "قیمت برآوردی نمی‌تواند منفی باشد.");
            }

            PurchaseRequest request;
            if (dto.Id.HasValue)
            {
                request = await _uow.Set<PurchaseRequest>().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == dto.Id.Value && !r.Deleted, cancellationToken);
                if (request == null) return ResponseDtoHelper.NotFound<PurchaseRequestDto>("درخواست خرید پیدا نشد.");
                if (request.Status != PurchaseRequestStatus.Draft) return Bad<PurchaseRequestDto>("فقط پیش‌نویس ویرایش می‌شود.");
                if (request.CreatorUserId != Me && !_user.IsInRole("PMO")) return Bad<PurchaseRequestDto>(NotAllowed);
                _uow.Set<PurchaseRequestLine>().RemoveRange(request.Lines);
                request.Lines = new List<PurchaseRequestLine>();
            }
            else
            {
                request = new PurchaseRequest
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.PurchaseRequest, "PRQ", dto.RequestDate, cancellationToken),
                    Status = PurchaseRequestStatus.Draft, CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Lines = new List<PurchaseRequestLine>(),
                };
                _uow.Set<PurchaseRequest>().Add(request);
            }
            request.Source = dto.Source;
            request.Kind = dto.Kind;
            request.RequestDate = dto.RequestDate.Date;
            request.WorkOrderId = dto.WorkOrderId;
            request.ContractId = dto.ContractId;
            request.CostCenter = Clean(dto.CostCenter);
            request.MaterialRequestId = dto.MaterialRequestId;
            request.Description = Clean(dto.Description);
            int rowNumber = 1;
            foreach (SavePurchaseRequestLineDto l in lines)
            {
                request.Lines.Add(new PurchaseRequestLine
                {
                    RowNumber = rowNumber++, ItemId = l.ItemId, Description = Clean(l.Description) ?? (l.ItemId.HasValue ? items[l.ItemId.Value].Title : null),
                    Unit = Clean(l.Unit) ?? "عدد", Quantity = l.Quantity, NeededBy = l.NeededBy?.Date, EstimatedUnitPrice = l.EstimatedUnitPrice,
                    TechnicalRequirements = Clean(l.TechnicalRequirements), BuyOrderId = l.BuyOrderId, MaterialRequestLineId = l.MaterialRequestLineId,
                });
            }
            request.EstimatedAmount = Math.Round(request.Lines.Sum(l => l.Quantity * (l.EstimatedUnitPrice ?? 0)), 0);
            if (dto.Submit)
            {
                request.Status = PurchaseRequestStatus.Submitted;
                request.SubmittedAt = DateTimeOffset.Now;
            }
            request.ModifyDate = DateTimeOffset.Now;
            request.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetRequestAsync(request.Id, cancellationToken);
        }

        public async Task<ResponseDto<PurchaseRequestDto>> RequestActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            PurchaseRequest r = await _uow.Set<PurchaseRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return ResponseDtoHelper.NotFound<PurchaseRequestDto>("درخواست خرید پیدا نشد.");
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (r.Status != PurchaseRequestStatus.Draft) return Bad<PurchaseRequestDto>("درخواست قبلاً ارسال شده است.");
                    r.Status = PurchaseRequestStatus.Submitted;
                    r.SubmittedAt = now;
                    break;
                case "approve-planning":
                    if (r.Status != PurchaseRequestStatus.Submitted) return Bad<PurchaseRequestDto>("فقط درخواست ارسال‌شده تایید می‌شود.");
                    if (!_user.IsInRole(ProcurementRoles.Planning)) return Bad<PurchaseRequestDto>(NotAllowed + " (برنامه‌ریزی و کنترل پروژه)");
                    if (r.CreatorUserId == Me || r.PlanningApprovedAt.HasValue) return Bad<PurchaseRequestDto>(SameUser);
                    r.PlanningApprovedAt = now;
                    r.PlanningApprovedByUserId = Me;
                    break;
                case "approve-management":
                    if (r.Status != PurchaseRequestStatus.Submitted) return Bad<PurchaseRequestDto>("فقط درخواست ارسال‌شده تایید می‌شود.");
                    if (!_user.IsInRole(ProcurementRoles.Management)) return Bad<PurchaseRequestDto>(NotAllowed + " (مدیرعامل)");
                    if (r.CreatorUserId == Me || r.PlanningApprovedByUserId == Me) return Bad<PurchaseRequestDto>(SameUser);
                    if (r.EstimatedAmount >= s[ProcurementSettingKeys.MajorFrom])
                    {
                        string board = Clean(dto.Reference);
                        if (board == null) return Bad<PurchaseRequestDto>("خرید عمده (BL-006 ماده 5) مصوبه هیئت‌مدیره لازم دارد؛ شماره مصوبه را بنویسید.");
                        r.BoardResolution = board;
                    }
                    r.ManagementApprovedAt = now;
                    r.ManagementApprovedByUserId = Me;
                    break;
                case "reject":
                    if (r.Status != PurchaseRequestStatus.Submitted) return Bad<PurchaseRequestDto>("فقط درخواست ارسال‌شده رد می‌شود.");
                    if (!_user.IsInRole(ProcurementRoles.Planning) && !_user.IsInRole(ProcurementRoles.Management)) return Bad<PurchaseRequestDto>(NotAllowed);
                    if (Clean(dto.Note) == null) return Bad<PurchaseRequestDto>("علت رد را بنویسید.");
                    r.Status = PurchaseRequestStatus.Rejected;
                    r.RejectedAt = now;
                    r.RejectedByUserId = Me;
                    r.RejectReason = Clean(dto.Note);
                    break;
                case "close":
                    if (r.Status != PurchaseRequestStatus.Approved && r.Status != PurchaseRequestStatus.Ordered) return Bad<PurchaseRequestDto>("فقط درخواست تاییدشده بسته می‌شود.");
                    if (!_user.IsInRole(ProcurementRoles.Planning)) return Bad<PurchaseRequestDto>(NotAllowed);
                    r.Status = PurchaseRequestStatus.Closed;
                    break;
                default:
                    return Bad<PurchaseRequestDto>("عمل نامعتبر است.");
            }
            // approved when planning has approved and, for medium and major purchases, management too (BL-006)
            if (r.Status == PurchaseRequestStatus.Submitted && r.PlanningApprovedAt.HasValue &&
                (r.EstimatedAmount < s[ProcurementSettingKeys.MediumFrom] || r.ManagementApprovedAt.HasValue))
                r.Status = PurchaseRequestStatus.Approved;
            r.ModifyDate = now;
            r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PurchaseRequestDto>("اطلاعات هم‌زمان تغییر کرد؛ دوباره تلاش کنید."); }
            return await GetRequestAsync(r.Id, cancellationToken);
        }

        // ------------------------------------------------------------------------------ inquiries

        private async Task<Dictionary<int, SupplierProfile>> ProfilesAsync(IEnumerable<int> providerIds, CancellationToken cancellationToken)
        {
            List<int> ids = providerIds.Distinct().ToList();
            return await _uow.Set<SupplierProfile>().Where(p => ids.Contains(p.ProviderId)).ToDictionaryAsync(p => p.ProviderId, cancellationToken);
        }

        private async Task<List<InquiryDto>> LoadInquiriesAsync(IQueryable<PurchaseInquiry> query, CancellationToken cancellationToken)
        {
            List<PurchaseInquiry> rows = await query.Include(i => i.PurchaseRequest)
                .Include(i => i.Quotes).ThenInclude(q => q.Provider)
                .Include(i => i.Quotes).ThenInclude(q => q.Lines)
                .AsSplitQuery().OrderByDescending(i => i.Id).ToListAsync(cancellationToken);
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            Dictionary<int, SupplierProfile> profiles = await ProfilesAsync(rows.SelectMany(i => i.Quotes.Select(q => q.ProviderId)), cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(i => new[]
            {
                i.FinancialApprovedByUserId, i.CommercialApprovedByUserId, i.TechnicalApprovedByUserId, i.ManagementApprovedByUserId,
            }), cancellationToken);
            List<int> ids = rows.Select(i => i.Id).ToList();
            var orders = await _uow.Set<PurchaseOrder>().Where(o => o.InquiryId.HasValue && ids.Contains(o.InquiryId.Value) && o.Status != PurchaseOrderStatus.Cancelled && !o.Deleted)
                .Select(o => new { o.Id, o.Number, InquiryId = o.InquiryId.Value }).ToListAsync(cancellationToken);
            var result = new List<InquiryDto>();
            foreach (PurchaseInquiry i in rows)
            {
                SupplierQuote chosen = i.Quotes.FirstOrDefault(q => q.Id == i.SelectedQuoteId);
                decimal amount = chosen?.TotalAmount ?? i.PurchaseRequest.EstimatedAmount;
                decimal lowest = i.Quotes.Where(q => q.TechnicallyAccepted).Select(q => q.TotalAmount).DefaultIfEmpty(0).Min();
                var order = orders.FirstOrDefault(o => o.InquiryId == i.Id);
                result.Add(new InquiryDto
                {
                    Id = i.Id, Number = i.Number, Status = i.Status, InquiryDate = i.InquiryDate, PurchaseRequestId = i.PurchaseRequestId,
                    PurchaseRequestNumber = i.PurchaseRequest.Number, Description = i.Description, RequiredQuotes = i.RequiredQuotes,
                    FewerQuotesReason = i.FewerQuotesReason, SelectedQuoteId = i.SelectedQuoteId, SelectionReason = i.SelectionReason,
                    NeedsCommittee = amount >= s[ProcurementSettingKeys.CommitteeFrom],
                    NeedsManagement = amount >= s[ProcurementSettingKeys.CeoCommitteeFrom] || (i.FewerQuotesReason != null && i.Quotes.Count < i.RequiredQuotes),
                    FinancialApprovedAt = i.FinancialApprovedAt, FinancialApprovedByName = names.Name(i.FinancialApprovedByUserId),
                    CommercialApprovedAt = i.CommercialApprovedAt, CommercialApprovedByName = names.Name(i.CommercialApprovedByUserId),
                    TechnicalApprovedAt = i.TechnicalApprovedAt, TechnicalApprovedByName = names.Name(i.TechnicalApprovedByUserId),
                    ManagementApprovedAt = i.ManagementApprovedAt, ManagementApprovedByName = names.Name(i.ManagementApprovedByUserId),
                    CommitteeMinutes = i.CommitteeMinutes, PurchaseOrderId = order?.Id, PurchaseOrderNumber = order?.Number,
                    Quotes = i.Quotes.OrderBy(q => q.TotalAmount).Select(q => new QuoteDto
                    {
                        Id = q.Id, ProviderId = q.ProviderId, ProviderName = q.Provider?.Name,
                        Grade = profiles.TryGetValue(q.ProviderId, out SupplierProfile p) ? p.Grade : SupplierGrade.NotEvaluated,
                        Blocked = profiles.TryGetValue(q.ProviderId, out SupplierProfile p2) && p2.Blocked,
                        QuoteNumber = q.QuoteNumber, QuoteDate = q.QuoteDate, ValidUntil = q.ValidUntil, DeliveryDays = q.DeliveryDays,
                        PaymentTerms = q.PaymentTerms, VatPercent = q.VatPercent, TechnicallyAccepted = q.TechnicallyAccepted,
                        TechnicalScore = q.TechnicalScore, TechnicalNote = q.TechnicalNote, DocumentId = q.DocumentId, Notes = q.Notes,
                        TotalAmount = q.TotalAmount, Lowest = q.TechnicallyAccepted && q.TotalAmount == lowest,
                        Lines = q.Lines.Select(l => new SaveQuoteLineDto { RequestLineId = l.RequestLineId, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList(),
                    }).ToList(),
                });
            }
            return result;
        }

        public async Task<ResponseDto<List<InquiryDto>>> GetInquiriesAsync(int? purchaseRequestId, CancellationToken cancellationToken)
        {
            IQueryable<PurchaseInquiry> q = _uow.Set<PurchaseInquiry>().Where(i => !i.Deleted);
            if (purchaseRequestId.HasValue) q = q.Where(i => i.PurchaseRequestId == purchaseRequestId.Value);
            return ResponseDtoHelper.Ok(await LoadInquiriesAsync(q.Take(500), cancellationToken));
        }

        public async Task<ResponseDto<InquiryDto>> GetInquiryAsync(int id, CancellationToken cancellationToken)
        {
            List<InquiryDto> list = await LoadInquiriesAsync(_uow.Set<PurchaseInquiry>().Where(i => i.Id == id && !i.Deleted), cancellationToken);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<InquiryDto>("استعلام پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<InquiryDto>> SaveInquiryAsync(SaveInquiryDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<InquiryDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<InquiryDto>(NotAllowed + " (بازرگانی)");
            PurchaseRequest request = await _uow.Set<PurchaseRequest>().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == dto.PurchaseRequestId && !r.Deleted, cancellationToken);
            if (request == null) return Bad<InquiryDto>("درخواست خرید پیدا نشد.");
            if (request.Status != PurchaseRequestStatus.Approved) return Bad<InquiryDto>("استعلام فقط برای درخواست خرید تاییدشده گرفته می‌شود.");
            if (dto.InquiryDate == default) dto.InquiryDate = DateTime.Today;

            List<SaveQuoteDto> quotes = (dto.Quotes ?? new List<SaveQuoteDto>()).Where(q => q != null).ToList();
            List<int> providerIds = quotes.Select(q => q.ProviderId).ToList();
            if (providerIds.Distinct().Count() != providerIds.Count) return Bad<InquiryDto>("از هر تامین‌کننده یک پیشنهاد ثبت می‌شود.");
            Dictionary<int, Provider> providers = await _uow.Set<Provider>().Where(p => providerIds.Contains(p.Id) && !p.Deleted).ToDictionaryAsync(p => p.Id, cancellationToken);
            foreach (SaveQuoteDto q in quotes)
            {
                if (!providers.ContainsKey(q.ProviderId)) return Bad<InquiryDto>("تامین‌کننده پیدا نشد.");
                if (q.Lines == null || q.Lines.Count == 0) return Bad<InquiryDto>($"پیشنهاد {providers[q.ProviderId].Name} ردیف قیمت ندارد.");
                foreach (SaveQuoteLineDto l in q.Lines)
                {
                    if (!request.Lines.Any(r => r.Id == l.RequestLineId)) return Bad<InquiryDto>("ردیف پیشنهاد متعلق به این درخواست نیست.");
                    if (l.Quantity <= 0 || l.UnitPrice < 0) return Bad<InquiryDto>("مقدار و قیمت پیشنهاد نامعتبر است.");
                }
            }

            PurchaseInquiry inquiry;
            if (dto.Id.HasValue)
            {
                inquiry = await _uow.Set<PurchaseInquiry>().Include(i => i.Quotes).ThenInclude(q => q.Lines).FirstOrDefaultAsync(i => i.Id == dto.Id.Value && !i.Deleted, cancellationToken);
                if (inquiry == null) return ResponseDtoHelper.NotFound<InquiryDto>("استعلام پیدا نشد.");
                if (inquiry.Status != InquiryStatus.Open) return Bad<InquiryDto>("بعد از انتخاب برنده، استعلام ویرایش نمی‌شود.");
                foreach (SupplierQuote q in inquiry.Quotes) _uow.Set<SupplierQuoteLine>().RemoveRange(q.Lines);
                _uow.Set<SupplierQuote>().RemoveRange(inquiry.Quotes);
                inquiry.Quotes = new List<SupplierQuote>();
            }
            else
            {
                inquiry = new PurchaseInquiry
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.Inquiry, "INQ", dto.InquiryDate, cancellationToken),
                    Status = InquiryStatus.Open, PurchaseRequestId = request.Id, CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Quotes = new List<SupplierQuote>(),
                };
                _uow.Set<PurchaseInquiry>().Add(inquiry);
            }
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            inquiry.InquiryDate = dto.InquiryDate.Date;
            inquiry.Description = Clean(dto.Description);
            inquiry.FewerQuotesReason = Clean(dto.FewerQuotesReason);
            inquiry.RequiredQuotes = ProcurementSettingKeys.RequiredQuotes(s, request.EstimatedAmount);
            foreach (SaveQuoteDto q in quotes)
            {
                inquiry.Quotes.Add(new SupplierQuote
                {
                    ProviderId = q.ProviderId, QuoteNumber = Clean(q.QuoteNumber), QuoteDate = q.QuoteDate == default ? inquiry.InquiryDate : q.QuoteDate.Date,
                    ValidUntil = q.ValidUntil?.Date, DeliveryDays = q.DeliveryDays, PaymentTerms = Clean(q.PaymentTerms), VatPercent = q.VatPercent,
                    TechnicallyAccepted = q.TechnicallyAccepted, TechnicalScore = q.TechnicalScore, TechnicalNote = Clean(q.TechnicalNote),
                    DocumentId = q.DocumentId, Notes = Clean(q.Notes),
                    TotalAmount = Math.Round(q.Lines.Sum(l => l.Quantity * l.UnitPrice), 0),
                    Lines = q.Lines.Select(l => new SupplierQuoteLine { RequestLineId = l.RequestLineId, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList(),
                });
            }
            inquiry.ModifyDate = DateTimeOffset.Now;
            inquiry.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetInquiryAsync(inquiry.Id, cancellationToken);
        }

        public async Task<ResponseDto<InquiryDto>> SelectQuoteAsync(SelectQuoteDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<InquiryDto>(NotAllowed + " (بازرگانی)");
            PurchaseInquiry inquiry = await _uow.Set<PurchaseInquiry>().Include(i => i.Quotes).Include(i => i.PurchaseRequest)
                .FirstOrDefaultAsync(i => i.Id == dto.InquiryId && !i.Deleted, cancellationToken);
            if (inquiry == null) return ResponseDtoHelper.NotFound<InquiryDto>("استعلام پیدا نشد.");
            if (inquiry.Status != InquiryStatus.Open) return Bad<InquiryDto>("برنده این استعلام قبلاً انتخاب شده است.");
            SupplierQuote quote = inquiry.Quotes.FirstOrDefault(q => q.Id == dto.QuoteId);
            if (quote == null) return Bad<InquiryDto>("پیشنهاد پیدا نشد.");

            // number of quotes (BL-007 s.5-4, BL-006 art. 4)
            int valid = inquiry.Quotes.Count(q => q.TechnicallyAccepted);
            if (valid < inquiry.RequiredQuotes && inquiry.FewerQuotesReason == null)
                return Bad<InquiryDto>($"برای این مبلغ {inquiry.RequiredQuotes} پیشنهاد قابل قبول فنی لازم است؛ اگر تامین‌کننده کمتری هست، دلیل آن را در استعلام بنویسید (تایید مدیرعامل لازم می‌شود).");
            if (!quote.TechnicallyAccepted) return Bad<InquiryDto>("پیشنهاد انتخاب‌شده تایید فنی ندارد.");

            // approved vendor list (WI-PO-003): D or blocked not allowed; C only when no A/B; not evaluated only under the petty-cash limit
            Dictionary<int, SupplierProfile> profiles = await ProfilesAsync(inquiry.Quotes.Select(q => q.ProviderId), cancellationToken);
            SupplierProfile profile = profiles.TryGetValue(quote.ProviderId, out SupplierProfile p) ? p : null;
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            if (profile?.Blocked == true || profile?.Grade == SupplierGrade.D)
                return Bad<InquiryDto>("این تامین‌کننده در فهرست غیرمجاز (گرید D یا مسدود) است و خرید از او مجاز نیست (WI-PO-003).");
            if ((profile == null || profile.Grade == SupplierGrade.NotEvaluated) && quote.TotalAmount > s[ProcurementSettingKeys.PettyCashLimit])
                return Bad<InquiryDto>("تامین‌کننده ارزیابی نشده است؛ پیش از سفارش، ارزیابی اولیه (FO-PO-003) لازم است.");
            if (profile?.Grade == SupplierGrade.C && inquiry.Quotes.Any(q => q.TechnicallyAccepted && profiles.TryGetValue(q.ProviderId, out SupplierProfile o) &&
                                                                              (o.Grade == SupplierGrade.A || o.Grade == SupplierGrade.B) && !o.Blocked))
                return Bad<InquiryDto>("تامین‌کننده گرید C فقط وقتی انتخاب می‌شود که تامین‌کننده A یا B نباشد.");

            decimal lowest = inquiry.Quotes.Where(q => q.TechnicallyAccepted).Min(q => q.TotalAmount);
            string reason = Clean(dto.Reason);
            if (quote.TotalAmount > lowest && reason == null)
                return Bad<InquiryDto>("پیشنهاد انتخاب‌شده کمترین قیمت قابل قبول نیست؛ دلیل انتخاب را بنویسید.");

            inquiry.SelectedQuoteId = quote.Id;
            inquiry.SelectionReason = reason;
            inquiry.SelectedAt = DateTimeOffset.Now;
            inquiry.SelectedByUserId = Me;
            inquiry.Status = InquiryStatus.Selected;
            UpdateInquiryStatus(inquiry, quote.TotalAmount, s);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetInquiryAsync(inquiry.Id, cancellationToken);
        }

        private static void UpdateInquiryStatus(PurchaseInquiry inquiry, decimal amount, Dictionary<string, decimal> s)
        {
            if (inquiry.Status != InquiryStatus.Selected) return;
            bool committee = amount >= s[ProcurementSettingKeys.CommitteeFrom];
            bool management = amount >= s[ProcurementSettingKeys.CeoCommitteeFrom] ||
                              (inquiry.FewerQuotesReason != null && inquiry.Quotes.Count(q => q.TechnicallyAccepted) < inquiry.RequiredQuotes);
            bool ok = (!committee || (inquiry.FinancialApprovedAt.HasValue && inquiry.CommercialApprovedAt.HasValue && inquiry.TechnicalApprovedAt.HasValue))
                      && (!management || inquiry.ManagementApprovedAt.HasValue);
            if (ok) inquiry.Status = InquiryStatus.Approved;
        }

        public async Task<ResponseDto<InquiryDto>> InquiryActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            PurchaseInquiry i = await _uow.Set<PurchaseInquiry>().Include(x => x.Quotes).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (i == null) return ResponseDtoHelper.NotFound<InquiryDto>("استعلام پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            string action = dto.Action?.Trim().ToLowerInvariant();
            if (action == "cancel")
            {
                if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<InquiryDto>(NotAllowed);
                if (await _uow.Set<PurchaseOrder>().AnyAsync(o => o.InquiryId == i.Id && o.Status != PurchaseOrderStatus.Cancelled && !o.Deleted, cancellationToken))
                    return Bad<InquiryDto>("از این استعلام سفارش صادر شده است.");
                i.Status = InquiryStatus.Cancelled;
                await _uow.SaveChangesAsync(cancellationToken);
                return await GetInquiryAsync(i.Id, cancellationToken);
            }
            if (i.Status != InquiryStatus.Selected) return Bad<InquiryDto>("تایید کمیته بعد از انتخاب برنده و پیش از صدور سفارش است.");
            var others = new[] { i.FinancialApprovedByUserId, i.CommercialApprovedByUserId, i.TechnicalApprovedByUserId, i.ManagementApprovedByUserId };
            if (others.Contains(Me) || i.SelectedByUserId == Me && action != "approve-commercial") return Bad<InquiryDto>(SameUser);
            switch (action)
            {
                case "approve-financial":
                    if (!_user.IsInRole(ProcurementRoles.Finance)) return Bad<InquiryDto>(NotAllowed + " (مدیر مالی یا معاونت PMO)");
                    i.FinancialApprovedAt = now; i.FinancialApprovedByUserId = Me; break;
                case "approve-commercial":
                    if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<InquiryDto>(NotAllowed + " (مدیر بازرگانی)");
                    i.CommercialApprovedAt = now; i.CommercialApprovedByUserId = Me; break;
                case "approve-technical":
                    if (!_user.IsInRole(ProcurementRoles.Technical)) return Bad<InquiryDto>(NotAllowed + " (مهندسی، کیفیت یا تولید)");
                    i.TechnicalApprovedAt = now; i.TechnicalApprovedByUserId = Me; break;
                case "approve-management":
                    if (!_user.IsInRole(ProcurementRoles.Management)) return Bad<InquiryDto>(NotAllowed + " (مدیرعامل)");
                    i.ManagementApprovedAt = now; i.ManagementApprovedByUserId = Me; break;
                default:
                    return Bad<InquiryDto>("عمل نامعتبر است.");
            }
            if (Clean(dto.Reference) != null) i.CommitteeMinutes = Clean(dto.Reference);
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            UpdateInquiryStatus(i, i.Quotes.First(q => q.Id == i.SelectedQuoteId).TotalAmount, s);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetInquiryAsync(i.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------- purchase orders

        public async Task<Dictionary<int, decimal>> AcceptedQuantitiesAsync(IEnumerable<int> orderLineIds, CancellationToken cancellationToken)
        {
            List<int> ids = orderLineIds.Distinct().ToList();
            var result = ids.ToDictionary(i => i, i => 0m);
            if (ids.Count == 0) return result;
            var receiptLines = await _uow.Set<InventoryDocumentLine>()
                .Where(l => l.PurchaseOrderLineId.HasValue && ids.Contains(l.PurchaseOrderLineId.Value) && l.Document.Status == InventoryDocumentStatus.Confirmed &&
                            !l.Document.Deleted && l.Document.Type == InventoryDocumentType.PurchaseReceipt)
                .Select(l => new { l.Id, Po = l.PurchaseOrderLineId.Value, l.Quantity }).ToListAsync(cancellationToken);
            List<int> receiptIds = receiptLines.Select(l => l.Id).ToList();
            var inspections = await _uow.Set<QcInspection>().Where(q => !q.Deleted && receiptIds.Contains(q.ReceiptLineId))
                .Select(q => new { q.Id, q.ReceiptLineId, q.Status, q.AcceptedQuantity }).ToListAsync(cancellationToken);
            List<int> inspectionIds = inspections.Select(q => q.Id).ToList();
            var released = await _uow.Set<NonConformance>()
                .Where(n => !n.Deleted && n.InspectionId.HasValue && inspectionIds.Contains(n.InspectionId.Value) &&
                            (n.Status == NcrStatus.Executed || n.Status == NcrStatus.Closed) &&
                            (n.Disposition == NcrDisposition.Concession || n.Disposition == NcrDisposition.Repair || n.Disposition == NcrDisposition.Rework))
                .Select(n => new { InspectionId = n.InspectionId.Value, n.Quantity }).ToListAsync(cancellationToken);
            foreach (var r in receiptLines)
            {
                var inspection = inspections.FirstOrDefault(q => q.ReceiptLineId == r.Id);
                decimal accepted = inspection == null ? r.Quantity
                    : (inspection.Status == InspectionStatus.Done ? inspection.AcceptedQuantity : 0) + released.Where(x => x.InspectionId == inspection.Id).Sum(x => x.Quantity);
                result[r.Po] += Math.Min(accepted, r.Quantity);
            }
            var services = await _uow.Set<ServiceAcceptance>().Where(a => !a.Deleted && ids.Contains(a.PurchaseOrderLineId))
                .GroupBy(a => a.PurchaseOrderLineId).Select(g => new { g.Key, Quantity = g.Sum(a => a.Quantity) }).ToListAsync(cancellationToken);
            foreach (var sv in services) result[sv.Key] += sv.Quantity;
            return result;
        }

        private async Task<Dictionary<int, decimal>> ReceivedQuantitiesAsync(List<int> ids, CancellationToken cancellationToken)
        {
            var rows = await _uow.Set<InventoryDocumentLine>()
                .Where(l => l.PurchaseOrderLineId.HasValue && ids.Contains(l.PurchaseOrderLineId.Value) && l.Document.Status == InventoryDocumentStatus.Confirmed &&
                            !l.Document.Deleted && (l.Document.Type == InventoryDocumentType.PurchaseReceipt || l.Document.Type == InventoryDocumentType.SupplierReturn))
                .Select(l => new { Po = l.PurchaseOrderLineId.Value, Quantity = l.Document.Type == InventoryDocumentType.SupplierReturn ? -l.Quantity : l.Quantity })
                .ToListAsync(cancellationToken);
            Dictionary<int, decimal> result = ids.ToDictionary(i => i, i => 0m);
            foreach (var r in rows) result[r.Po] += r.Quantity;
            var services = await _uow.Set<ServiceAcceptance>().Where(a => !a.Deleted && ids.Contains(a.PurchaseOrderLineId))
                .GroupBy(a => a.PurchaseOrderLineId).Select(g => new { g.Key, Quantity = g.Sum(a => a.Quantity) }).ToListAsync(cancellationToken);
            foreach (var sv in services) result[sv.Key] += sv.Quantity;
            return result;
        }

        private async Task<List<PurchaseOrderDto>> LoadOrdersAsync(IQueryable<PurchaseOrder> query, CancellationToken cancellationToken)
        {
            List<PurchaseOrder> rows = await query.Include(o => o.Provider).Include(o => o.Inquiry).Include(o => o.WorkOrder).Include(o => o.Contract)
                .Include(o => o.Lines).ThenInclude(l => l.Item)
                .Include(o => o.Lines).ThenInclude(l => l.RequestLine).ThenInclude(r => r.PurchaseRequest)
                .AsSplitQuery().ToListAsync(cancellationToken);
            List<int> lineIds = rows.SelectMany(o => o.Lines.Select(l => l.Id)).ToList();
            Dictionary<int, decimal> accepted = await AcceptedQuantitiesAsync(lineIds, cancellationToken);
            Dictionary<int, SupplierProfile> profiles = await ProfilesAsync(rows.Select(o => o.ProviderId), cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(o => new int?[] { o.CreatorUserId, o.ApprovedByUserId }), cancellationToken);
            List<int> orderIds = rows.Select(o => o.Id).ToList();
            var paid = await _uow.Set<PaymentRequest>().Where(p => p.PurchaseOrderId.HasValue && orderIds.Contains(p.PurchaseOrderId.Value) && p.Status == PaymentRequestStatus.Paid && !p.Deleted)
                .GroupBy(p => p.PurchaseOrderId.Value).Select(g => new { g.Key, Amount = g.Sum(p => p.Amount - p.Deductions) }).ToListAsync(cancellationToken);
            var result = new List<PurchaseOrderDto>();
            foreach (PurchaseOrder o in rows)
            {
                var dto = new PurchaseOrderDto
                {
                    Id = o.Id, Number = o.Number, Status = o.Status, Kind = o.Kind, OrderDate = o.OrderDate, ProviderId = o.ProviderId, ProviderName = o.Provider?.Name,
                    Grade = profiles.TryGetValue(o.ProviderId, out SupplierProfile p) ? p.Grade : SupplierGrade.NotEvaluated,
                    InquiryId = o.InquiryId, InquiryNumber = o.Inquiry?.Number, WorkOrderId = o.WorkOrderId, WorkOrderCode = o.WorkOrder?.Code,
                    ContractId = o.ContractId, ContractCode = o.Contract?.Code, Currency = o.Currency, ExchangeRate = o.ExchangeRate, Incoterms = o.Incoterms,
                    PaymentTerms = o.PaymentTerms, VatPercent = o.VatPercent, AdvancePercent = o.AdvancePercent, AdvanceGuarantee = o.AdvanceGuarantee,
                    AdvanceGuaranteeExpiry = o.AdvanceGuaranteeExpiry, PerformanceGuarantee = o.PerformanceGuarantee, RetentionPercent = o.RetentionPercent,
                    ContractNumber = o.ContractNumber, ContractSignedAt = o.ContractSignedAt, Requirements = o.Requirements, DeliveryDate = o.DeliveryDate,
                    DeliveryPlace = o.DeliveryPlace, LatePenaltyPercentPerWeek = o.LatePenaltyPercentPerWeek, Description = o.Description,
                    TotalAmount = o.TotalAmount, VatAmount = Math.Round(o.TotalAmount * (o.VatPercent ?? 0) / 100m, 0),
                    CreateDate = o.CreateDate, CreatorUserId = o.CreatorUserId, CreatorName = names.Name(o.CreatorUserId),
                    ApprovedAt = o.ApprovedAt, ApprovedByUserId = o.ApprovedByUserId, ApprovedByName = names.Name(o.ApprovedByUserId),
                    ReleasedAt = o.ReleasedAt, ClosedAt = o.ClosedAt, CancelledAt = o.CancelledAt, CancelReason = o.CancelReason,
                    PaidAmount = paid.FirstOrDefault(x => x.Key == o.Id)?.Amount ?? 0,
                };
                foreach (PurchaseOrderLine l in o.Lines.OrderBy(x => x.RowNumber))
                {
                    decimal acc = accepted.TryGetValue(l.Id, out decimal a) ? a : 0;
                    dto.Lines.Add(new PurchaseOrderLineDto
                    {
                        Id = l.Id, RowNumber = l.RowNumber, RequestLineId = l.RequestLineId, RequestNumber = l.RequestLine?.PurchaseRequest?.Number,
                        ItemId = l.ItemId, ItemCode = l.Item?.Code, ItemTitle = l.Item?.Title, Description = l.Description, Unit = l.Unit,
                        Quantity = l.Quantity, UnitPrice = l.UnitPrice, Amount = Math.Round(l.Quantity * l.UnitPrice, 0), DeliveryDate = l.DeliveryDate,
                        WorkOrderId = l.WorkOrderId, ContractId = l.ContractId, IsCapital = l.IsCapital, ReceivedQuantity = l.ReceivedQuantity, AcceptedQuantity = acc,
                        InvoicedQuantity = l.InvoicedQuantity, OpenQuantity = Math.Max(0, l.Quantity - l.ReceivedQuantity), LastReceiptDate = l.LastReceiptDate,
                        Late = (l.DeliveryDate ?? o.DeliveryDate).HasValue && l.ReceivedQuantity < l.Quantity && (l.DeliveryDate ?? o.DeliveryDate) < DateTime.Today &&
                               o.Status != PurchaseOrderStatus.Closed && o.Status != PurchaseOrderStatus.Cancelled,
                    });
                }
                dto.ReceivedAmount = Math.Round(dto.Lines.Sum(l => l.AcceptedQuantity * l.UnitPrice), 0);
                dto.InvoicedAmount = Math.Round(dto.Lines.Sum(l => l.InvoicedQuantity * l.UnitPrice), 0);
                result.Add(dto);
            }
            return result;
        }

        public async Task<ResponseDto<PagedList<PurchaseOrderDto>>> GetOrdersAsync(PurchaseOrderQuery query, CancellationToken cancellationToken)
        {
            query ??= new PurchaseOrderQuery();
            IQueryable<PurchaseOrder> orders = _uow.Set<PurchaseOrder>().Where(o => !o.Deleted);
            if (query.Status.HasValue) orders = orders.Where(o => o.Status == query.Status.Value);
            if (query.ProviderId.HasValue) orders = orders.Where(o => o.ProviderId == query.ProviderId.Value);
            if (query.OpenForReceipt == true) orders = orders.Where(o => o.Status == PurchaseOrderStatus.Released || o.Status == PurchaseOrderStatus.PartlyReceived);
            string text = Clean(query.Contains);
            if (text != null) orders = orders.Where(o => o.Number.Contains(text) || o.Provider.Name.Contains(text) || o.ContractNumber.Contains(text) ||
                                                         o.Lines.Any(l => l.Item.Code.Contains(text) || l.Description.Contains(text)));
            int total = await orders.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize;
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<PurchaseOrderDto> list = await LoadOrdersAsync(orders.OrderByDescending(o => o.Id).Skip((number - 1) * size).Take(size), cancellationToken);
            PagedList<PurchaseOrderDto> page = await PagedList<PurchaseOrderDto>.CreateAsync(list.OrderByDescending(o => o.Id).ToList(), 1, 0, cancellationToken);
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)size);
            return ResponseDtoHelper.Ok(page);
        }

        public async Task<ResponseDto<PurchaseOrderDto>> GetOrderAsync(int id, CancellationToken cancellationToken)
        {
            List<PurchaseOrderDto> list = await LoadOrdersAsync(_uow.Set<PurchaseOrder>().Where(o => o.Id == id && !o.Deleted), cancellationToken);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<PurchaseOrderDto>("سفارش خرید پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<PurchaseOrderDto>> CreateOrderFromInquiryAsync(int inquiryId, CancellationToken cancellationToken)
        {
            PurchaseInquiry inquiry = await _uow.Set<PurchaseInquiry>().Include(i => i.PurchaseRequest).ThenInclude(r => r.Lines)
                .Include(i => i.Quotes).ThenInclude(q => q.Lines).FirstOrDefaultAsync(i => i.Id == inquiryId && !i.Deleted, cancellationToken);
            if (inquiry == null) return ResponseDtoHelper.NotFound<PurchaseOrderDto>("استعلام پیدا نشد.");
            if (inquiry.Status != InquiryStatus.Approved) return Bad<PurchaseOrderDto>("سفارش فقط از استعلام تاییدشده (برنده و تایید کمیته) صادر می‌شود.");
            SupplierQuote quote = inquiry.Quotes.First(q => q.Id == inquiry.SelectedQuoteId);
            PurchaseRequest request = inquiry.PurchaseRequest;
            var dto = new SavePurchaseOrderDto
            {
                Kind = request.Kind, OrderDate = DateTime.Today, ProviderId = quote.ProviderId, InquiryId = inquiry.Id, WorkOrderId = request.WorkOrderId,
                ContractId = request.ContractId, PaymentTerms = quote.PaymentTerms, VatPercent = quote.VatPercent,
                DeliveryDate = quote.DeliveryDays.HasValue ? DateTime.Today.AddDays(quote.DeliveryDays.Value) : (DateTime?)null,
                Requirements = string.Join("\n", request.Lines.Where(l => l.TechnicalRequirements != null).Select(l => $"ردیف {l.RowNumber}: {l.TechnicalRequirements}")),
                Lines = quote.Lines.Select(ql =>
                {
                    PurchaseRequestLine rl = request.Lines.First(r => r.Id == ql.RequestLineId);
                    return new SavePurchaseOrderLineDto
                    {
                        RequestLineId = rl.Id, ItemId = rl.ItemId, Description = rl.Description, Unit = rl.Unit, Quantity = ql.Quantity, UnitPrice = ql.UnitPrice,
                        DeliveryDate = rl.NeededBy, WorkOrderId = request.WorkOrderId, ContractId = request.ContractId,
                    };
                }).ToList(),
            };
            return await SaveOrderAsync(dto, cancellationToken);
        }

        public async Task<ResponseDto<PurchaseOrderDto>> SaveOrderAsync(SavePurchaseOrderDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PurchaseOrderDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<PurchaseOrderDto>(NotAllowed + " (بازرگانی سفارش را تهیه می‌کند)");
            if (dto.OrderDate == default) dto.OrderDate = DateTime.Today;
            if (!await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId && !p.Deleted, cancellationToken)) return Bad<PurchaseOrderDto>("تامین‌کننده پیدا نشد.");
            SupplierProfile profile = await _uow.Set<SupplierProfile>().FirstOrDefaultAsync(p => p.ProviderId == dto.ProviderId, cancellationToken);
            if (profile?.Blocked == true || profile?.Grade == SupplierGrade.D) return Bad<PurchaseOrderDto>("خرید از تامین‌کننده غیرمجاز (گرید D یا مسدود) ممنوع است.");
            decimal rate = 1;
            string currency = Clean(dto.Currency)?.ToUpperInvariant();
            if (currency != null && currency != "IRR")
            {
                if (!dto.ExchangeRate.HasValue || dto.ExchangeRate <= 0) return Bad<PurchaseOrderDto>("برای خرید ارزی نرخ تبدیل به ریال را وارد کنید.");
                rate = dto.ExchangeRate.Value;
            }
            else currency = null;

            List<SavePurchaseOrderLineDto> lines = (dto.Lines ?? new List<SavePurchaseOrderLineDto>()).Where(l => l != null).ToList();
            if (lines.Count == 0) return Bad<PurchaseOrderDto>("سفارش حداقل یک ردیف لازم دارد.");
            List<int> requestLineIds = lines.Where(l => l.RequestLineId.HasValue).Select(l => l.RequestLineId.Value).ToList();
            var requestLines = await _uow.Set<PurchaseRequestLine>().Where(l => requestLineIds.Contains(l.Id))
                .Select(l => new { l.Id, l.ItemId, l.Quantity, Status = l.PurchaseRequest.Status, l.PurchaseRequestId }).ToListAsync(cancellationToken);
            var alreadyOrdered = await _uow.Set<PurchaseOrderLine>()
                .Where(l => l.RequestLineId.HasValue && requestLineIds.Contains(l.RequestLineId.Value) && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled &&
                            !l.PurchaseOrder.Deleted && l.PurchaseOrderId != (dto.Id ?? 0))
                .GroupBy(l => l.RequestLineId.Value).Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) }).ToListAsync(cancellationToken);
            for (int i = 0; i < lines.Count; i++)
            {
                SavePurchaseOrderLineDto l = lines[i];
                string row = $"ردیف {i + 1}: ";
                if (l.Quantity <= 0 || l.UnitPrice < 0) return Bad<PurchaseOrderDto>(row + "مقدار یا قیمت نامعتبر است.");
                if (!l.RequestLineId.HasValue) return Bad<PurchaseOrderDto>(row + "هر ردیف سفارش باید از یک درخواست خرید تاییدشده باشد.");
                var rl = requestLines.FirstOrDefault(r => r.Id == l.RequestLineId.Value);
                if (rl == null) return Bad<PurchaseOrderDto>(row + "ردیف درخواست خرید پیدا نشد.");
                if (rl.Status != PurchaseRequestStatus.Approved && rl.Status != PurchaseRequestStatus.Ordered) return Bad<PurchaseOrderDto>(row + "درخواست خرید تایید نشده است.");
                if (rl.ItemId != l.ItemId) return Bad<PurchaseOrderDto>(row + "کالای سفارش با درخواست خرید یکی نیست.");
                decimal before = alreadyOrdered.FirstOrDefault(o => o.Key == rl.Id)?.Quantity ?? 0;
                if (before + l.Quantity > rl.Quantity) return Bad<PurchaseOrderDto>(row + $"مقدار بیشتر از مانده درخواست خرید ({rl.Quantity - before}) است.");
            }
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            if (dto.AdvancePercent < 0 || dto.AdvancePercent > 100) return Bad<PurchaseOrderDto>("درصد پیش‌پرداخت نامعتبر است.");
            if (dto.AdvancePercent > 0 && Clean(dto.AdvanceGuarantee) == null) return Bad<PurchaseOrderDto>("پیش‌پرداخت در برابر ضمانت‌نامه است (BL-006 ماده 8)؛ مشخصات ضمانت‌نامه را بنویسید.");

            PurchaseOrder order;
            if (dto.Id.HasValue)
            {
                order = await _uow.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == dto.Id.Value && !o.Deleted, cancellationToken);
                if (order == null) return ResponseDtoHelper.NotFound<PurchaseOrderDto>("سفارش خرید پیدا نشد.");
                if (order.Status != PurchaseOrderStatus.Draft) return Bad<PurchaseOrderDto>("فقط پیش‌نویس سفارش ویرایش می‌شود.");
                _uow.Set<PurchaseOrderLine>().RemoveRange(order.Lines);
                order.Lines = new List<PurchaseOrderLine>();
            }
            else
            {
                order = new PurchaseOrder
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.PurchaseOrder, "PO", dto.OrderDate, cancellationToken),
                    Status = PurchaseOrderStatus.Draft, CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Lines = new List<PurchaseOrderLine>(),
                };
                _uow.Set<PurchaseOrder>().Add(order);
            }
            order.Kind = dto.Kind;
            order.OrderDate = dto.OrderDate.Date;
            order.ProviderId = dto.ProviderId;
            order.InquiryId = dto.InquiryId;
            order.WorkOrderId = dto.WorkOrderId;
            order.ContractId = dto.ContractId;
            order.Currency = currency;
            order.ExchangeRate = currency == null ? null : dto.ExchangeRate;
            order.Incoterms = Clean(dto.Incoterms);
            order.PaymentTerms = Clean(dto.PaymentTerms);
            order.VatPercent = dto.VatPercent;
            order.AdvancePercent = dto.AdvancePercent;
            order.AdvanceGuarantee = Clean(dto.AdvanceGuarantee);
            order.AdvanceGuaranteeExpiry = dto.AdvanceGuaranteeExpiry;
            order.PerformanceGuarantee = Clean(dto.PerformanceGuarantee);
            order.RetentionPercent = dto.RetentionPercent;
            order.ContractNumber = Clean(dto.ContractNumber);
            order.ContractSignedAt = dto.ContractSignedAt;
            order.Requirements = Clean(dto.Requirements);
            order.DeliveryDate = dto.DeliveryDate?.Date;
            order.DeliveryPlace = Clean(dto.DeliveryPlace);
            order.LatePenaltyPercentPerWeek = dto.LatePenaltyPercentPerWeek;
            order.Description = Clean(dto.Description);
            int rowNumber = 1;
            foreach (SavePurchaseOrderLineDto l in lines)
            {
                order.Lines.Add(new PurchaseOrderLine
                {
                    RowNumber = rowNumber++, RequestLineId = l.RequestLineId, ItemId = l.ItemId, Description = Clean(l.Description), Unit = Clean(l.Unit) ?? "عدد",
                    Quantity = l.Quantity, UnitPrice = Math.Round(l.UnitPrice * rate, 4), DeliveryDate = l.DeliveryDate?.Date ?? order.DeliveryDate,
                    WorkOrderId = l.WorkOrderId ?? dto.WorkOrderId, ContractId = l.ContractId ?? dto.ContractId, IsCapital = l.IsCapital,
                });
            }
            order.TotalAmount = Math.Round(order.Lines.Sum(l => l.Quantity * l.UnitPrice), 0);
            order.ModifyDate = DateTimeOffset.Now;
            order.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetOrderAsync(order.Id, cancellationToken);
        }

        public async Task<ResponseDto<PurchaseOrderDto>> OrderActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            PurchaseOrder o = await _uow.Set<PurchaseOrder>().Include(x => x.Lines).ThenInclude(l => l.RequestLine)
                .FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (o == null) return ResponseDtoHelper.NotFound<PurchaseOrderDto>("سفارش خرید پیدا نشد.");
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "approve":
                {
                    // ISO 9001 8.4.3: the order is reviewed before it is communicated - by someone other than the buyer
                    if (o.Status != PurchaseOrderStatus.Draft) return Bad<PurchaseOrderDto>("فقط پیش‌نویس تایید می‌شود.");
                    if (!_user.IsInRole(ProcurementRoles.OrderApproval)) return Bad<PurchaseOrderDto>(NotAllowed + " (معاونت PMO)");
                    if (o.CreatorUserId == Me) return Bad<PurchaseOrderDto>(SameUser);
                    if (o.TotalAmount >= s[ProcurementSettingKeys.Quote1From] && !o.InquiryId.HasValue)
                        return Bad<PurchaseOrderDto>("برای این مبلغ سفارش باید از استعلام تاییدشده صادر شود.");
                    if (o.TotalAmount >= s[ProcurementSettingKeys.ContractFrom] && (o.ContractNumber == null || !o.ContractSignedAt.HasValue))
                        return Bad<PurchaseOrderDto>("خرید بالاتر از سقف قرارداد (WI-PL-004 گام 7) قرارداد امضاشده لازم دارد؛ شماره و تاریخ قرارداد را ثبت کنید.");
                    if ((o.AdvancePercent ?? 0) > s[ProcurementSettingKeys.MaxAdvancePercent] && !_user.IsInRole("CEO"))
                        return Bad<PurchaseOrderDto>($"پیش‌پرداخت بیش از {s[ProcurementSettingKeys.MaxAdvancePercent]}٪ تایید مدیرعامل لازم دارد.");
                    if (o.Kind != PurchaseKind.Service && o.Requirements == null)
                        return Bad<PurchaseOrderDto>("الزامات فنی و کیفی سفارش (مشخصات، گواهی مواد، بازرسی) را بنویسید (ISO 9001 8.4.3).");
                    if (o.Currency != null && !_user.IsInRole("CEO")) return Bad<PurchaseOrderDto>("سفارش خارجی تایید مدیرعامل لازم دارد (PR-IP-005 بخش 7).");
                    o.Status = PurchaseOrderStatus.Approved;
                    o.ApprovedAt = now;
                    o.ApprovedByUserId = Me;
                    break;
                }
                case "release":
                    if (o.Status != PurchaseOrderStatus.Approved) return Bad<PurchaseOrderDto>("سفارش باید اول تایید شود.");
                    if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<PurchaseOrderDto>(NotAllowed);
                    o.Status = PurchaseOrderStatus.Released;
                    o.ReleasedAt = now;
                    o.ReleasedByUserId = Me;
                    foreach (int requestId in o.Lines.Where(l => l.RequestLine != null).Select(l => l.RequestLine.PurchaseRequestId).Distinct())
                    {
                        PurchaseRequest r = await _uow.Set<PurchaseRequest>().FirstAsync(x => x.Id == requestId, cancellationToken);
                        if (r.Status == PurchaseRequestStatus.Approved) r.Status = PurchaseRequestStatus.Ordered;
                    }
                    break;
                case "close":
                    if (o.Status != PurchaseOrderStatus.PartlyReceived && o.Status != PurchaseOrderStatus.Received && o.Status != PurchaseOrderStatus.Released)
                        return Bad<PurchaseOrderDto>("این سفارش بسته نمی‌شود.");
                    if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<PurchaseOrderDto>(NotAllowed);
                    o.Status = PurchaseOrderStatus.Closed;
                    o.ClosedAt = now;
                    break;
                case "cancel":
                    if (o.Status != PurchaseOrderStatus.Draft && o.Status != PurchaseOrderStatus.Approved && o.Status != PurchaseOrderStatus.Released)
                        return Bad<PurchaseOrderDto>("سفارشی که کالای آن رسیده باطل نمی‌شود؛ آن را ببندید.");
                    if (o.Lines.Any(l => l.ReceivedQuantity != 0)) return Bad<PurchaseOrderDto>("برای این سفارش رسید ثبت شده است.");
                    if (!_user.IsInRole(ProcurementRoles.Commercial) && !_user.IsInRole(ProcurementRoles.OrderApproval)) return Bad<PurchaseOrderDto>(NotAllowed);
                    if (Clean(dto.Note) == null) return Bad<PurchaseOrderDto>("علت ابطال را بنویسید.");
                    o.Status = PurchaseOrderStatus.Cancelled;
                    o.CancelledAt = now;
                    o.CancelReason = Clean(dto.Note);
                    break;
                default:
                    return Bad<PurchaseOrderDto>("عمل نامعتبر است.");
            }
            o.ModifyDate = now;
            o.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PurchaseOrderDto>("اطلاعات هم‌زمان تغییر کرد؛ دوباره تلاش کنید."); }
            return await GetOrderAsync(o.Id, cancellationToken);
        }

        public async Task RefreshOrderAsync(int purchaseOrderId, CancellationToken cancellationToken)
        {
            PurchaseOrder o = await _uow.Set<PurchaseOrder>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == purchaseOrderId, cancellationToken);
            if (o == null) return;
            List<int> ids = o.Lines.Select(l => l.Id).ToList();
            Dictionary<int, decimal> received = await ReceivedQuantitiesAsync(ids, cancellationToken);
            var last = await _uow.Set<InventoryDocumentLine>()
                .Where(l => l.PurchaseOrderLineId.HasValue && ids.Contains(l.PurchaseOrderLineId.Value) && l.Document.Status == InventoryDocumentStatus.Confirmed &&
                            l.Document.Type == InventoryDocumentType.PurchaseReceipt)
                .GroupBy(l => l.PurchaseOrderLineId.Value).Select(g => new { g.Key, Date = g.Max(l => l.Document.DocumentDate) }).ToListAsync(cancellationToken);
            foreach (PurchaseOrderLine l in o.Lines)
            {
                l.ReceivedQuantity = received[l.Id];
                l.LastReceiptDate = last.FirstOrDefault(x => x.Key == l.Id)?.Date;
            }
            if (o.Status == PurchaseOrderStatus.Released || o.Status == PurchaseOrderStatus.PartlyReceived || o.Status == PurchaseOrderStatus.Received)
            {
                o.Status = o.Lines.All(l => l.ReceivedQuantity >= l.Quantity) ? PurchaseOrderStatus.Received
                    : o.Lines.Any(l => l.ReceivedQuantity > 0) ? PurchaseOrderStatus.PartlyReceived
                    : PurchaseOrderStatus.Released;
            }
        }

        public async Task<ResponseDto<PurchaseOrderDto>> AcceptServiceAsync(SaveServiceAcceptanceDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.ServiceAcceptance)) return Bad<PurchaseOrderDto>(NotAllowed + " (کنترل کیفیت یا واحد درخواست‌کننده)");
            PurchaseOrderLine line = await _uow.Set<PurchaseOrderLine>().Include(l => l.PurchaseOrder)
                .FirstOrDefaultAsync(l => l.Id == dto.PurchaseOrderLineId, cancellationToken);
            if (line == null) return ResponseDtoHelper.NotFound<PurchaseOrderDto>("ردیف سفارش پیدا نشد.");
            PurchaseOrder o = line.PurchaseOrder;
            if (o.Kind == PurchaseKind.Goods || line.ItemId.HasValue) return Bad<PurchaseOrderDto>("کالا با رسید انبار تحویل می‌شود؛ تایید انجام خدمت برای خدمات و پیمانکاری است.");
            if (o.Status != PurchaseOrderStatus.Released && o.Status != PurchaseOrderStatus.PartlyReceived) return Bad<PurchaseOrderDto>("سفارش ابلاغ نشده یا بسته است.");
            if (o.CreatorUserId == Me || o.ReleasedByUserId == Me) return Bad<PurchaseOrderDto>("خریدار نمی‌تواند انجام خدمت را تایید کند (تفکیک وظایف).");
            if (dto.Quantity <= 0) return Bad<PurchaseOrderDto>("مقدار باید بیشتر از صفر باشد.");
            Dictionary<int, decimal> received = await ReceivedQuantitiesAsync(new List<int> { line.Id }, cancellationToken);
            if (received[line.Id] + dto.Quantity > line.Quantity) return Bad<PurchaseOrderDto>($"مانده این ردیف {line.Quantity - received[line.Id]} است.");
            DateTime date = dto.AcceptanceDate == default ? DateTime.Today : dto.AcceptanceDate.Date;
            var acceptance = new ServiceAcceptance
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.ServiceAcceptance, "SA", date, cancellationToken),
                PurchaseOrderLineId = line.Id, AcceptanceDate = date, Quantity = dto.Quantity, Notes = Clean(dto.Notes), DocumentId = dto.DocumentId,
                CreateDate = DateTimeOffset.Now, CreatorUserId = Me, ModifyDate = DateTimeOffset.Now, ModifierUserId = Me,
            };
            _uow.Set<ServiceAcceptance>().Add(acceptance);

            // cost of the work order / project and the accounting event: service received, not yet invoiced
            decimal amount = Math.Round(dto.Quantity * line.UnitPrice, 0);
            bool forWork = (line.WorkOrderId ?? o.WorkOrderId).HasValue || (line.ContractId ?? o.ContractId).HasValue;
            if (amount > 0)
            {
                _uow.Set<CostEntry>().Add(new CostEntry
                {
                    EntryDate = date, Kind = o.Kind == PurchaseKind.Subcontract ? CostKind.Subcontract : CostKind.PurchasedService,
                    WorkOrderId = line.WorkOrderId ?? o.WorkOrderId, ContractId = line.ContractId ?? o.ContractId, Quantity = dto.Quantity, Amount = amount,
                    SourceType = "ServiceAcceptance", SourceId = line.Id, SourceNumber = acceptance.Number, Description = line.Description,
                    CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me,
                });
                _costing.AddEvent(AccountingEventType.PurchaseReceipt, date, "ServiceAcceptance", line.Id, acceptance.Number, "تایید خدمت " + o.Number, new[]
                {
                    new AccountingEventLine
                    {
                        AccountRole = forWork ? AccountRoles.Wip : AccountRoles.PurchasedServices, Debit = amount,
                        WorkOrderId = line.WorkOrderId ?? o.WorkOrderId, ContractId = line.ContractId ?? o.ContractId, Description = line.Description,
                    },
                    new AccountingEventLine { AccountRole = AccountRoles.Grni, Credit = amount, ProviderId = o.ProviderId },
                });
            }
            await _uow.SaveChangesAsync(cancellationToken);
            await RefreshOrderAsync(o.Id, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetOrderAsync(o.Id, cancellationToken);
        }
    }
}
