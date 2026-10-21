using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
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
    public interface IPayablesService
    {
        Task<ResponseDto<PagedList<SupplierInvoiceDto>>> GetInvoicesAsync(ProcurementListQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<SupplierInvoiceDto>> GetInvoiceAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<SupplierInvoiceDto>> SaveInvoiceAsync(SaveSupplierInvoiceDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<SupplierInvoiceDto>> InvoiceActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PaymentRequestDto>>> GetPaymentsAsync(ProcurementListQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PaymentRequestDto>> SavePaymentAsync(SavePaymentRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PaymentRequestDto>> PaymentActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<SupplierProfileDto>>> GetSuppliersAsync(string contains, CancellationToken cancellationToken);
        Task<ResponseDto<SupplierProfileDto>> SaveSupplierAsync(SaveSupplierProfileDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<SupplierEvaluationDto>>> GetEvaluationsAsync(int providerId, CancellationToken cancellationToken);
        Task<ResponseDto<SupplierEvaluationDto>> EvaluateAsync(SaveSupplierEvaluationDto dto, CancellationToken cancellationToken);

        /// <summary>A supplier return was confirmed: warning to the supplier; the third one blocks it (WI-PO-003 note 3).</summary>
        Task OnSupplierReturnAsync(int providerId, string documentNumber, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Supplier invoices (three-way match: order, accepted receipt, invoice), payment requests to finance
    /// and the approved vendor list with supplier evaluations.
    /// </summary>
    public class PayablesService : IPayablesService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ICostingService _costing;
        private readonly ITreasuryService _treasury;
        private readonly IProcurementService _procurement;

        public PayablesService(IUnitOfWork uow, IInventoryUserContext user, ICostingService costing, IProcurementService procurement, ITreasuryService treasury)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _costing = costing ?? throw new ArgumentNullException(nameof(costing));
            _treasury = treasury ?? throw new ArgumentNullException(nameof(treasury));
            _procurement = procurement ?? throw new ArgumentNullException(nameof(procurement));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "این کار باید توسط شخص دیگری غیر از ثبت‌کننده یا تاییدکننده قبلی انجام شود.";

        private static PagedList<T> Page<T>(List<T> all, int total, int number, int size)
        {
            PagedList<T> page = PagedList<T>.CreateAsync(all, 1, 0, CancellationToken.None).GetAwaiter().GetResult();
            page.TotalCount = total; page.CurrentPage = number; page.PageSize = size; page.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return page;
        }

        // -------------------------------------------------------------------------------- invoices

        private async Task<List<SupplierInvoiceDto>> LoadInvoicesAsync(IQueryable<SupplierInvoice> query, CancellationToken cancellationToken)
        {
            List<SupplierInvoice> rows = await query.Include(i => i.Provider).Include(i => i.PurchaseOrder)
                .Include(i => i.Lines).ThenInclude(l => l.OrderLine).ThenInclude(ol => ol.Item).AsSplitQuery().ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(i => new int?[] { i.CreatorUserId, i.ApprovedByUserId }), cancellationToken);
            return rows.Select(i => new SupplierInvoiceDto
            {
                Id = i.Id, Number = i.Number, Status = i.Status, ProviderId = i.ProviderId, ProviderName = i.Provider?.Name, PurchaseOrderId = i.PurchaseOrderId,
                PurchaseOrderNumber = i.PurchaseOrder?.Number, InvoiceNumber = i.InvoiceNumber, InvoiceDate = i.InvoiceDate, IsOfficial = i.IsOfficial,
                NetAmount = i.NetAmount, VatAmount = i.VatAmount, TotalAmount = i.TotalAmount, PriceVariance = i.PriceVariance, MatchNote = i.MatchNote,
                DocumentId = i.DocumentId, CreateDate = i.CreateDate, CreatorName = names.Name(i.CreatorUserId), ApprovedAt = i.ApprovedAt,
                ApprovedByName = names.Name(i.ApprovedByUserId), CancelReason = i.CancelReason,
                Lines = i.Lines.Select(l => new SupplierInvoiceLineDto
                {
                    Id = l.Id, OrderLineId = l.OrderLineId, ItemCode = l.OrderLine?.Item?.Code, Description = l.OrderLine?.Description, Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice, OrderUnitPrice = l.OrderLine?.UnitPrice ?? 0, Amount = Math.Round(l.Quantity * l.UnitPrice, 0),
                }).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<SupplierInvoiceDto>>> GetInvoicesAsync(ProcurementListQuery query, CancellationToken cancellationToken)
        {
            query ??= new ProcurementListQuery();
            IQueryable<SupplierInvoice> q = _uow.Set<SupplierInvoice>().Where(i => !i.Deleted);
            if (query.Status.HasValue) q = q.Where(i => (int)i.Status == query.Status.Value);
            if (query.ProviderId.HasValue) q = q.Where(i => i.ProviderId == query.ProviderId.Value);
            if (query.PurchaseOrderId.HasValue) q = q.Where(i => i.PurchaseOrderId == query.PurchaseOrderId.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(i => i.Number.Contains(text) || i.InvoiceNumber.Contains(text) || i.Provider.Name.Contains(text));
            int total = await q.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<SupplierInvoiceDto> list = await LoadInvoicesAsync(q.OrderByDescending(i => i.Id).Skip((number - 1) * size).Take(size), cancellationToken);
            return ResponseDtoHelper.Ok(Page(list.OrderByDescending(i => i.Id).ToList(), total, number, size));
        }

        public async Task<ResponseDto<SupplierInvoiceDto>> GetInvoiceAsync(int id, CancellationToken cancellationToken)
        {
            List<SupplierInvoiceDto> list = await LoadInvoicesAsync(_uow.Set<SupplierInvoice>().Where(i => i.Id == id && !i.Deleted), cancellationToken);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<SupplierInvoiceDto>("فاکتور پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<SupplierInvoiceDto>> SaveInvoiceAsync(SaveSupplierInvoiceDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<SupplierInvoiceDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Finance) && !_user.IsInRole(ProcurementRoles.Commercial)) return Bad<SupplierInvoiceDto>(NotAllowed);
            PurchaseOrder order = await _uow.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == dto.PurchaseOrderId && !o.Deleted, cancellationToken);
            if (order == null) return Bad<SupplierInvoiceDto>("سفارش خرید پیدا نشد.");
            if (order.Status < PurchaseOrderStatus.Released || order.Status == PurchaseOrderStatus.Cancelled) return Bad<SupplierInvoiceDto>("فاکتور فقط برای سفارش ابلاغ‌شده ثبت می‌شود.");
            string invoiceNumber = Clean(dto.InvoiceNumber);
            if (invoiceNumber == null) return Bad<SupplierInvoiceDto>("شماره فاکتور تامین‌کننده را وارد کنید.");
            if (await _uow.Set<SupplierInvoice>().AnyAsync(i => !i.Deleted && i.ProviderId == order.ProviderId && i.InvoiceNumber == invoiceNumber &&
                                                               i.Status != SupplierInvoiceStatus.Cancelled && i.Id != (dto.Id ?? 0), cancellationToken))
                return Bad<SupplierInvoiceDto>("این فاکتور قبلاً ثبت شده است (جلوگیری از پرداخت تکراری).");
            if (dto.InvoiceDate == default) return Bad<SupplierInvoiceDto>("تاریخ فاکتور را وارد کنید.");
            List<SaveSupplierInvoiceLineDto> lines = (dto.Lines ?? new List<SaveSupplierInvoiceLineDto>()).Where(l => l != null && l.Quantity != 0).ToList();
            if (lines.Count == 0) return Bad<SupplierInvoiceDto>("فاکتور حداقل یک ردیف لازم دارد.");

            // three-way match: only quantities accepted by QC (or services accepted), not yet invoiced
            Dictionary<int, decimal> accepted = await _procurement.AcceptedQuantitiesAsync(order.Lines.Select(l => l.Id), cancellationToken);
            var invoiced = await _uow.Set<SupplierInvoiceLine>()
                .Where(l => l.OrderLine.PurchaseOrderId == order.Id && l.Invoice.Status != SupplierInvoiceStatus.Cancelled && !l.Invoice.Deleted && l.InvoiceId != (dto.Id ?? 0))
                .GroupBy(l => l.OrderLineId).Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) }).ToListAsync(cancellationToken);
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            decimal tolerance = s[ProcurementSettingKeys.PriceTolerancePercent] / 100m;
            var notes = new List<string>();
            decimal variance = 0;
            bool outOfTolerance = false;
            foreach (var g in lines.GroupBy(l => l.OrderLineId))
            {
                PurchaseOrderLine ol = order.Lines.FirstOrDefault(l => l.Id == g.Key);
                if (ol == null) return Bad<SupplierInvoiceDto>("ردیف فاکتور متعلق به این سفارش نیست.");
                decimal quantity = g.Sum(l => l.Quantity);
                decimal open = accepted[ol.Id] - (invoiced.FirstOrDefault(x => x.Key == ol.Id)?.Quantity ?? 0);
                if (quantity < 0 || quantity > open)
                    return Bad<SupplierInvoiceDto>($"ردیف {ol.RowNumber}: فقط {Math.Max(0, open)} از این ردیف تایید کیفی شده و فاکتور نشده است (فاکتور پیش از تعیین وضعیت کیفی پذیرفته نمی‌شود).");
                foreach (SaveSupplierInvoiceLineDto l in g)
                {
                    if (l.UnitPrice < 0) return Bad<SupplierInvoiceDto>("قیمت نامعتبر است.");
                    decimal diff = (l.UnitPrice - ol.UnitPrice) * l.Quantity;
                    variance += diff;
                    if (ol.UnitPrice > 0 && Math.Abs(l.UnitPrice - ol.UnitPrice) / ol.UnitPrice > tolerance)
                    {
                        outOfTolerance = true;
                        notes.Add($"ردیف {ol.RowNumber}: قیمت فاکتور {l.UnitPrice:0} با قیمت سفارش {ol.UnitPrice:0} فرق دارد.");
                    }
                }
            }

            SupplierInvoice invoice;
            if (dto.Id.HasValue)
            {
                invoice = await _uow.Set<SupplierInvoice>().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == dto.Id.Value && !i.Deleted, cancellationToken);
                if (invoice == null) return ResponseDtoHelper.NotFound<SupplierInvoiceDto>("فاکتور پیدا نشد.");
                if (invoice.Status == SupplierInvoiceStatus.Approved || invoice.Status == SupplierInvoiceStatus.Cancelled) return Bad<SupplierInvoiceDto>("فاکتور تاییدشده ویرایش نمی‌شود.");
                _uow.Set<SupplierInvoiceLine>().RemoveRange(invoice.Lines);
                invoice.Lines = new List<SupplierInvoiceLine>();
            }
            else
            {
                invoice = new SupplierInvoice
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.Invoice, "SINV", dto.InvoiceDate, cancellationToken),
                    CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Lines = new List<SupplierInvoiceLine>(),
                };
                _uow.Set<SupplierInvoice>().Add(invoice);
            }
            invoice.ProviderId = order.ProviderId;
            invoice.PurchaseOrderId = order.Id;
            invoice.InvoiceNumber = invoiceNumber;
            invoice.InvoiceDate = dto.InvoiceDate.Date;
            invoice.IsOfficial = dto.IsOfficial;
            invoice.DocumentId = dto.DocumentId;
            foreach (SaveSupplierInvoiceLineDto l in lines)
                invoice.Lines.Add(new SupplierInvoiceLine { OrderLineId = l.OrderLineId, Quantity = l.Quantity, UnitPrice = l.UnitPrice });
            invoice.NetAmount = Math.Round(lines.Sum(l => l.Quantity * l.UnitPrice), 0);
            invoice.VatAmount = Math.Round(dto.VatAmount ?? invoice.NetAmount * (order.VatPercent ?? 0) / 100m, 0);
            invoice.TotalAmount = invoice.NetAmount + invoice.VatAmount;
            invoice.PriceVariance = Math.Round(variance, 0);
            invoice.MatchNote = notes.Count == 0 ? "مطابق سفارش و رسید تاییدشده" : string.Join(" | ", notes);
            invoice.Status = outOfTolerance ? SupplierInvoiceStatus.NeedsApproval : SupplierInvoiceStatus.Matched;
            invoice.ModifyDate = DateTimeOffset.Now;
            invoice.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetInvoiceAsync(invoice.Id, cancellationToken);
        }

        public async Task<ResponseDto<SupplierInvoiceDto>> InvoiceActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            SupplierInvoice invoice = await _uow.Set<SupplierInvoice>().Include(i => i.Lines).ThenInclude(l => l.OrderLine).Include(i => i.PurchaseOrder)
                .FirstOrDefaultAsync(i => i.Id == dto.Id && !i.Deleted, cancellationToken);
            if (invoice == null) return ResponseDtoHelper.NotFound<SupplierInvoiceDto>("فاکتور پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "approve":
                {
                    if (invoice.Status != SupplierInvoiceStatus.Matched && invoice.Status != SupplierInvoiceStatus.NeedsApproval) return Bad<SupplierInvoiceDto>("این فاکتور در مرحله تایید نیست.");
                    if (invoice.CreatorUserId == Me) return Bad<SupplierInvoiceDto>(SameUser);
                    string role = invoice.Status == SupplierInvoiceStatus.NeedsApproval ? ProcurementRoles.OrderApproval : ProcurementRoles.Finance;
                    if (!_user.IsInRole(role)) return Bad<SupplierInvoiceDto>(NotAllowed + (invoice.Status == SupplierInvoiceStatus.NeedsApproval ? " (اختلاف قیمت: معاونت PMO)" : " (مالی)"));
                    invoice.Status = SupplierInvoiceStatus.Approved;
                    invoice.ApprovedAt = now;
                    invoice.ApprovedByUserId = Me;
                    foreach (SupplierInvoiceLine l in invoice.Lines) l.OrderLine.InvoicedQuantity += l.Quantity;

                    // accounting: goods/services received not invoiced are cleared; the difference is a price variance; payable to the supplier
                    decimal grni = Math.Round(invoice.Lines.Sum(l => l.Quantity * l.OrderLine.UnitPrice), 0);
                    decimal ppv = invoice.NetAmount - grni;
                    _costing.AddEvent(AccountingEventType.SupplierInvoice, invoice.InvoiceDate, "SupplierInvoice", invoice.Id, invoice.Number,
                        $"فاکتور {invoice.InvoiceNumber} - سفارش {invoice.PurchaseOrder.Number}", new[]
                        {
                            new AccountingEventLine { AccountRole = AccountRoles.Grni, Debit = grni, ProviderId = invoice.ProviderId },
                            new AccountingEventLine
                            {
                                AccountRole = AccountRoles.PurchasePriceVariance, Debit = ppv > 0 ? ppv : 0, Credit = ppv < 0 ? -ppv : 0,
                                WorkOrderId = invoice.PurchaseOrder.WorkOrderId, ContractId = invoice.PurchaseOrder.ContractId,
                            },
                            new AccountingEventLine { AccountRole = AccountRoles.Vat, Debit = invoice.VatAmount },
                            new AccountingEventLine { AccountRole = AccountRoles.AccountsPayable, Credit = invoice.TotalAmount, ProviderId = invoice.ProviderId, Description = invoice.InvoiceNumber },
                        });

                    // capital lines: a draft fixed asset is opened for finance to complete (class, cost centre, in-use date); the invoice event above is unchanged
                    foreach (SupplierInvoiceLine l in invoice.Lines.Where(x => x.OrderLine.IsCapital))
                        await AssetDrafts.AddFromInvoiceLineAsync(_uow, invoice, l, Me, cancellationToken);
                    break;
                }
                case "cancel":
                    if (invoice.Status == SupplierInvoiceStatus.Approved) return Bad<SupplierInvoiceDto>("فاکتور تاییدشده باطل نمی‌شود؛ برگشت را در سپیدار ثبت کنید.");
                    if (Clean(dto.Note) == null) return Bad<SupplierInvoiceDto>("علت ابطال را بنویسید.");
                    invoice.Status = SupplierInvoiceStatus.Cancelled;
                    invoice.CancelledAt = now;
                    invoice.CancelReason = Clean(dto.Note);
                    break;
                default:
                    return Bad<SupplierInvoiceDto>("عمل نامعتبر است.");
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetInvoiceAsync(invoice.Id, cancellationToken);
        }

        // -------------------------------------------------------------------------------- payments

        private async Task<List<PaymentRequestDto>> LoadPaymentsAsync(IQueryable<PaymentRequest> query, CancellationToken cancellationToken)
        {
            List<PaymentRequest> rows = await query.Include(p => p.Provider).Include(p => p.PurchaseOrder).Include(p => p.Invoice).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(p => new int?[] { p.CreatorUserId, p.ApprovedByUserId, p.PaidByUserId }), cancellationToken);
            List<int> requestIds = rows.Select(p => p.Id).ToList();
            List<FnPaymentDeduction> typed = await _uow.Set<FnPaymentDeduction>().AsNoTracking().Where(d => requestIds.Contains(d.PaymentRequestId)).OrderBy(d => d.Id).ToListAsync(cancellationToken);
            return rows.Select(p => new PaymentRequestDto
            {
                Id = p.Id, Number = p.Number, Type = p.Type, Status = p.Status, RequestDate = p.RequestDate, ProviderId = p.ProviderId, ProviderName = p.Provider?.Name,
                PurchaseOrderId = p.PurchaseOrderId, PurchaseOrderNumber = p.PurchaseOrder?.Number, InvoiceId = p.InvoiceId, InvoiceNumber = p.Invoice?.InvoiceNumber,
                Amount = p.Amount, Deductions = p.Deductions, NetAmount = p.Amount - p.Deductions, DeductionNote = p.DeductionNote, QcApproved = p.QcApproved,
                Description = p.Description, CreateDate = p.CreateDate, CreatorName = names.Name(p.CreatorUserId), ApprovedAt = p.ApprovedAt,
                ApprovedByName = names.Name(p.ApprovedByUserId), PaidAt = p.PaidAt, PaidByName = names.Name(p.PaidByUserId), PaymentReference = p.PaymentReference,
                RejectReason = p.RejectReason,
                DeductionLines = typed.Where(d => d.PaymentRequestId == p.Id).Select(d => new PaymentDeductionDto { Kind = d.Kind, Base = d.Base, RatePercent = d.RatePercent, Amount = d.Amount, Note = d.Note }).ToList(),
            }).ToList();
        }

        public async Task<ResponseDto<PagedList<PaymentRequestDto>>> GetPaymentsAsync(ProcurementListQuery query, CancellationToken cancellationToken)
        {
            query ??= new ProcurementListQuery();
            IQueryable<PaymentRequest> q = _uow.Set<PaymentRequest>().Where(p => !p.Deleted);
            if (query.Status.HasValue) q = q.Where(p => (int)p.Status == query.Status.Value);
            if (query.ProviderId.HasValue) q = q.Where(p => p.ProviderId == query.ProviderId.Value);
            if (query.PurchaseOrderId.HasValue) q = q.Where(p => p.PurchaseOrderId == query.PurchaseOrderId.Value);
            int total = await q.CountAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(total, 1) : query.PageSize, number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<PaymentRequestDto> list = await LoadPaymentsAsync(q.OrderByDescending(p => p.Id).Skip((number - 1) * size).Take(size), cancellationToken);
            return ResponseDtoHelper.Ok(Page(list.OrderByDescending(p => p.Id).ToList(), total, number, size));
        }

        public async Task<ResponseDto<PaymentRequestDto>> SavePaymentAsync(SavePaymentRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PaymentRequestDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<PaymentRequestDto>(NotAllowed + " (بازرگانی)");
            if (dto.Amount <= 0 || dto.Deductions < 0 || dto.Deductions > dto.Amount) return Bad<PaymentRequestDto>("مبلغ یا کسورات نامعتبر است.");
            List<PaymentDeductionDto> typedLines = (dto.DeductionLines ?? new List<PaymentDeductionDto>()).Where(d => d != null && d.Amount != 0).ToList();
            if (typedLines.Any(d => d.Amount < 0 || !Enum.IsDefined(typeof(FnDeductionKind), d.Kind))) return Bad<PaymentRequestDto>("کسورات تفکیک‌شده نامعتبر است.");
            decimal typedSum = typedLines.Sum(d => Math.Round(d.Amount, 0));
            decimal totalDeductions = dto.Deductions == 0 && typedSum > 0 ? typedSum : Math.Round(dto.Deductions, 0);
            if (typedSum > totalDeductions) return Bad<PaymentRequestDto>("مجموع کسورات تفکیک‌شده از کل کسورات بیشتر است.");
            if (totalDeductions > dto.Amount) return Bad<PaymentRequestDto>("مبلغ یا کسورات نامعتبر است.");
            PurchaseOrder order = dto.PurchaseOrderId.HasValue
                ? await _uow.Set<PurchaseOrder>().FirstOrDefaultAsync(o => o.Id == dto.PurchaseOrderId.Value && !o.Deleted, cancellationToken) : null;
            SupplierInvoice invoice = dto.InvoiceId.HasValue
                ? await _uow.Set<SupplierInvoice>().FirstOrDefaultAsync(i => i.Id == dto.InvoiceId.Value && !i.Deleted, cancellationToken) : null;
            if (order == null && invoice == null) return Bad<PaymentRequestDto>("سفارش یا فاکتور را مشخص کنید.");
            if (invoice != null && order != null && invoice.PurchaseOrderId != order.Id) return Bad<PaymentRequestDto>("فاکتور متعلق به این سفارش نیست.");
            if (invoice != null) order ??= await _uow.Set<PurchaseOrder>().FirstAsync(o => o.Id == invoice.PurchaseOrderId, cancellationToken);
            bool qcApproved = false;
            switch (dto.Type)
            {
                case PaymentRequestType.Advance:
                {
                    if (order.Status < PurchaseOrderStatus.Released || order.Status == PurchaseOrderStatus.Cancelled) return Bad<PaymentRequestDto>("پیش‌پرداخت برای سفارش ابلاغ‌شده است.");
                    decimal allowed = Math.Round(order.TotalAmount * (order.AdvancePercent ?? 0) / 100m, 0);
                    decimal before = await _uow.Set<PaymentRequest>().Where(p => p.PurchaseOrderId == order.Id && p.Type == PaymentRequestType.Advance &&
                                                                                 p.Status != PaymentRequestStatus.Rejected && !p.Deleted && p.Id != (dto.Id ?? 0))
                        .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0;
                    if (before + dto.Amount > allowed) return Bad<PaymentRequestDto>($"پیش‌پرداخت مجاز این سفارش {allowed:0} ریال است (درصد پیش‌پرداخت در سفارش، با ضمانت‌نامه).");
                    break;
                }
                case PaymentRequestType.Invoice:
                case PaymentRequestType.Final:
                {
                    if (invoice == null || invoice.Status != SupplierInvoiceStatus.Approved) return Bad<PaymentRequestDto>("پرداخت فقط برای فاکتور تاییدشده (تطبیق با سفارش و رسید تاییدشده کیفی) است.");
                    decimal before = await _uow.Set<PaymentRequest>().Where(p => p.InvoiceId == invoice.Id && p.Status != PaymentRequestStatus.Rejected && !p.Deleted && p.Id != (dto.Id ?? 0))
                        .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0;
                    if (before + dto.Amount > invoice.TotalAmount) return Bad<PaymentRequestDto>("مبلغ از مانده فاکتور بیشتر است.");
                    qcApproved = true;
                    break;
                }
                case PaymentRequestType.Progress:
                {
                    // payment statement: only for work accepted by QC / the requesting unit
                    decimal acceptedAmount = 0;
                    List<PurchaseOrderLine> lines = await _uow.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == order.Id).ToListAsync(cancellationToken);
                    Dictionary<int, decimal> accepted = await _procurement.AcceptedQuantitiesAsync(lines.Select(l => l.Id), cancellationToken);
                    foreach (PurchaseOrderLine l in lines) acceptedAmount += accepted[l.Id] * l.UnitPrice;
                    decimal before = await _uow.Set<PaymentRequest>().Where(p => p.PurchaseOrderId == order.Id && p.Status != PaymentRequestStatus.Rejected && !p.Deleted &&
                                                                                 p.Id != (dto.Id ?? 0) && p.Type != PaymentRequestType.Advance)
                        .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0;
                    if (before + dto.Amount > Math.Round(acceptedAmount, 0)) return Bad<PaymentRequestDto>($"صورت‌وضعیت بیش از کار تاییدشده کیفی ({acceptedAmount:0} ریال) است.");
                    qcApproved = true;
                    break;
                }
                default:
                    return Bad<PaymentRequestDto>("نوع پرداخت نامعتبر است.");
            }

            PaymentRequest payment;
            if (dto.Id.HasValue)
            {
                payment = await _uow.Set<PaymentRequest>().FirstOrDefaultAsync(p => p.Id == dto.Id.Value && !p.Deleted, cancellationToken);
                if (payment == null) return ResponseDtoHelper.NotFound<PaymentRequestDto>("درخواست پرداخت پیدا نشد.");
                if (payment.Status != PaymentRequestStatus.Draft) return Bad<PaymentRequestDto>("فقط پیش‌نویس ویرایش می‌شود.");
            }
            else
            {
                payment = new PaymentRequest
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.Payment, "PAY", DateTime.Today, cancellationToken),
                    Status = PaymentRequestStatus.Draft, CreateDate = DateTimeOffset.Now, CreatorUserId = Me,
                };
                _uow.Set<PaymentRequest>().Add(payment);
            }
            payment.Type = dto.Type;
            payment.RequestDate = dto.RequestDate == default ? DateTime.Today : dto.RequestDate.Date;
            payment.ProviderId = order.ProviderId;
            payment.PurchaseOrderId = order.Id;
            payment.InvoiceId = invoice?.Id;
            payment.Amount = Math.Round(dto.Amount, 0);
            payment.Deductions = totalDeductions;
            if (dto.Id.HasValue) _uow.Set<FnPaymentDeduction>().RemoveRange(await _uow.Set<FnPaymentDeduction>().Where(d => d.PaymentRequestId == payment.Id).ToListAsync(cancellationToken));
            foreach (PaymentDeductionDto d in typedLines)
                _uow.Set<FnPaymentDeduction>().Add(new FnPaymentDeduction
                {
                    PaymentRequest = payment, Kind = d.Kind, Base = Math.Round(d.Base, 0), RatePercent = d.RatePercent, Amount = Math.Round(d.Amount, 0), Note = d.Note?.Trim(),
                });
            payment.DeductionNote = Clean(dto.DeductionNote);
            payment.QcApproved = qcApproved;
            payment.Description = Clean(dto.Description);
            payment.ModifyDate = DateTimeOffset.Now;
            payment.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await LoadPaymentsAsync(_uow.Set<PaymentRequest>().Where(p => p.Id == payment.Id), cancellationToken))[0]);
        }

        public async Task<ResponseDto<PaymentRequestDto>> PaymentActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            PaymentRequest p = await _uow.Set<PaymentRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (p == null) return ResponseDtoHelper.NotFound<PaymentRequestDto>("درخواست پرداخت پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "approve":
                    if (p.Status != PaymentRequestStatus.Draft) return Bad<PaymentRequestDto>("این درخواست در مرحله تایید نیست.");
                    if (!_user.IsInRole(ProcurementRoles.OrderApproval)) return Bad<PaymentRequestDto>(NotAllowed + " (معاونت PMO)");
                    if (p.CreatorUserId == Me) return Bad<PaymentRequestDto>(SameUser);
                    p.Status = PaymentRequestStatus.Approved;
                    p.ApprovedAt = now;
                    p.ApprovedByUserId = Me;
                    break;
                case "paid":
                    if (p.Status != PaymentRequestStatus.Approved) return Bad<PaymentRequestDto>("فقط درخواست تاییدشده پرداخت می‌شود.");
                    if (!_user.IsInRole(ProcurementRoles.Finance)) return Bad<PaymentRequestDto>(NotAllowed + " (مالی)");
                    if (p.CreatorUserId == Me || p.ApprovedByUserId == Me) return Bad<PaymentRequestDto>(SameUser);
                    if (Clean(dto.Reference) == null) return Bad<PaymentRequestDto>("شماره سند پرداخت در سپیدار یا شماره پیگیری بانک را وارد کنید.");
                    string legacyError = await _treasury.AddLegacyPaymentAsync(p, dto.Reference, cancellationToken);
                    if (legacyError != null) return Bad<PaymentRequestDto>(legacyError);
                    p.Status = PaymentRequestStatus.Paid;
                    p.PaidAt = now;
                    p.PaidByUserId = Me;
                    p.PaymentReference = Clean(dto.Reference);
                    break;
                case "reject":
                    if (p.Status == PaymentRequestStatus.Paid) return Bad<PaymentRequestDto>("پرداخت انجام شده است.");
                    if (!_user.IsInRole(ProcurementRoles.OrderApproval) && !_user.IsInRole(ProcurementRoles.Finance)) return Bad<PaymentRequestDto>(NotAllowed);
                    if (Clean(dto.Note) == null) return Bad<PaymentRequestDto>("علت رد را بنویسید.");
                    p.Status = PaymentRequestStatus.Rejected;
                    p.RejectedAt = now;
                    p.RejectReason = Clean(dto.Note);
                    break;
                default:
                    return Bad<PaymentRequestDto>("عمل نامعتبر است.");
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await LoadPaymentsAsync(_uow.Set<PaymentRequest>().Where(x => x.Id == p.Id), cancellationToken))[0]);
        }

        // ------------------------------------------------------------------------------- suppliers

        public async Task<ResponseDto<List<SupplierProfileDto>>> GetSuppliersAsync(string contains, CancellationToken cancellationToken)
        {
            IQueryable<Provider> providers = _uow.Set<Provider>().Where(p => !p.Deleted);
            string text = Clean(contains);
            if (text != null) providers = providers.Where(p => p.Name.Contains(text));
            var list = await providers.OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).Take(1000).ToListAsync(cancellationToken);
            List<int> ids = list.Select(p => p.Id).ToList();
            Dictionary<int, SupplierProfile> profiles = await _uow.Set<SupplierProfile>().Where(p => ids.Contains(p.ProviderId)).ToDictionaryAsync(p => p.ProviderId, cancellationToken);
            var orders = await _uow.Set<PurchaseOrder>().Where(o => ids.Contains(o.ProviderId) && !o.Deleted && o.Status != PurchaseOrderStatus.Cancelled && o.Status != PurchaseOrderStatus.Draft)
                .GroupBy(o => o.ProviderId).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(o => o.TotalAmount) }).ToListAsync(cancellationToken);
            var lines = await _uow.Set<PurchaseOrderLine>().Where(l => ids.Contains(l.PurchaseOrder.ProviderId) && l.LastReceiptDate.HasValue)
                .Select(l => new { l.PurchaseOrder.ProviderId, Late = (l.DeliveryDate ?? l.PurchaseOrder.DeliveryDate) < l.LastReceiptDate }).ToListAsync(cancellationToken);
            var ncrs = await _uow.Set<NonConformance>().Where(n => !n.Deleted && n.ProviderId.HasValue && ids.Contains(n.ProviderId.Value))
                .GroupBy(n => n.ProviderId.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            var returns = await _uow.Set<InventoryDocument>().Where(d => !d.Deleted && d.Type == InventoryDocumentType.SupplierReturn && d.Status == InventoryDocumentStatus.Confirmed &&
                                                                         d.ProviderId.HasValue && ids.Contains(d.ProviderId.Value))
                .GroupBy(d => d.ProviderId.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            var result = list.Select(p =>
            {
                profiles.TryGetValue(p.Id, out SupplierProfile pr);
                var ls = lines.Where(l => l.ProviderId == p.Id).ToList();
                return new SupplierProfileDto
                {
                    ProviderId = p.Id, ProviderName = p.Name, Grade = pr?.Grade ?? SupplierGrade.NotEvaluated, Score = pr?.Score, EvaluatedAt = pr?.EvaluatedAt,
                    NextEvaluation = pr?.NextEvaluation, EvaluationDue = pr?.NextEvaluation.HasValue == true && pr.NextEvaluation <= DateTime.Today,
                    Categories = pr?.Categories, IsStrategic = pr?.IsStrategic ?? false, Blocked = pr?.Blocked ?? false, BlockReason = pr?.BlockReason,
                    Warnings = pr?.Warnings ?? 0, SepidarCode = pr?.SepidarCode, NationalId = pr?.NationalId, EconomicCode = pr?.EconomicCode, Iban = pr?.Iban,
                    Orders = orders.FirstOrDefault(o => o.Key == p.Id)?.Count ?? 0, OrderedAmount = orders.FirstOrDefault(o => o.Key == p.Id)?.Amount ?? 0,
                    ReceiptLines = ls.Count, LateReceiptLines = ls.Count(l => l.Late),
                    OnTimePercent = ls.Count == 0 ? (decimal?)null : Math.Round(100m * ls.Count(l => !l.Late) / ls.Count, 1),
                    Ncrs = ncrs.FirstOrDefault(n => n.Key == p.Id)?.Count ?? 0, Returns = returns.FirstOrDefault(r => r.Key == p.Id)?.Count ?? 0,
                };
            }).ToList();
            return ResponseDtoHelper.Ok(result);
        }

        private async Task<SupplierProfile> ProfileAsync(int providerId, CancellationToken cancellationToken)
        {
            SupplierProfile profile = await _uow.Set<SupplierProfile>().FirstOrDefaultAsync(p => p.ProviderId == providerId, cancellationToken);
            if (profile == null)
            {
                profile = new SupplierProfile { ProviderId = providerId, Grade = SupplierGrade.NotEvaluated };
                _uow.Set<SupplierProfile>().Add(profile);
            }
            return profile;
        }

        public async Task<ResponseDto<SupplierProfileDto>> SaveSupplierAsync(SaveSupplierProfileDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<SupplierProfileDto>(NotAllowed);
            if (!await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId && !p.Deleted, cancellationToken)) return Bad<SupplierProfileDto>("تامین‌کننده پیدا نشد.");
            SupplierProfile profile = await ProfileAsync(dto.ProviderId, cancellationToken);
            // unblocking a grade-D or warned supplier needs management (WI-PO-003)
            if (profile.Blocked && !dto.Blocked && !_user.IsInRole(ProcurementRoles.Management)) return Bad<SupplierProfileDto>("رفع مسدودی تامین‌کننده با مدیرعامل است.");
            if (dto.Blocked && Clean(dto.BlockReason) == null) return Bad<SupplierProfileDto>("علت مسدودی را بنویسید.");
            profile.Categories = Clean(dto.Categories);
            profile.IsStrategic = dto.IsStrategic;
            profile.Blocked = dto.Blocked || profile.Grade == SupplierGrade.D;
            profile.BlockReason = profile.Blocked ? Clean(dto.BlockReason) ?? profile.BlockReason : null;
            profile.SepidarCode = Clean(dto.SepidarCode);
            profile.NationalId = Clean(dto.NationalId);
            profile.EconomicCode = Clean(dto.EconomicCode);
            string iban = Clean(dto.Iban)?.Replace(" ", "").ToUpperInvariant();
            if (iban != null && !Finance.LedgerMath.IbanValid(Finance.LedgerMath.Digits(iban))) return Bad<SupplierProfileDto>("شماره شبا نامعتبر است.");
            profile.Iban = iban == null ? null : Finance.LedgerMath.Digits(iban);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await GetSuppliersAsync(null, cancellationToken)).Data.First(p => p.ProviderId == dto.ProviderId));
        }

        public async Task<ResponseDto<List<SupplierEvaluationDto>>> GetEvaluationsAsync(int providerId, CancellationToken cancellationToken)
        {
            List<SupplierEvaluation> rows = await _uow.Set<SupplierEvaluation>().Include(e => e.Provider).Include(e => e.Lines)
                .Where(e => e.ProviderId == providerId && !e.Deleted).OrderByDescending(e => e.EvaluationDate).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(e => (int?)e.CreatorUserId), cancellationToken);
            return ResponseDtoHelper.Ok(rows.Select(e => new SupplierEvaluationDto
            {
                Id = e.Id, ProviderId = e.ProviderId, ProviderName = e.Provider?.Name, Kind = e.Kind, EvaluationDate = e.EvaluationDate, PurchaseOrderId = e.PurchaseOrderId,
                Score = e.Score, Grade = e.Grade, Notes = e.Notes, EvaluatorName = names.Name(e.CreatorUserId),
                Lines = e.Lines.Select(l => new EvaluationLineDto { Question = l.Question, Weight = l.Weight, Score = l.Score }).ToList(),
            }).ToList());
        }

        public async Task<ResponseDto<SupplierEvaluationDto>> EvaluateAsync(SaveSupplierEvaluationDto dto, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProcurementRoles.Commercial) && !_user.IsInRole(ProcurementRoles.Technical)) return Bad<SupplierEvaluationDto>(NotAllowed);
            if (!await _uow.Set<Provider>().AnyAsync(p => p.Id == dto.ProviderId && !p.Deleted, cancellationToken)) return Bad<SupplierEvaluationDto>("تامین‌کننده پیدا نشد.");
            List<EvaluationLineDto> lines = (dto.Lines ?? new List<EvaluationLineDto>()).Where(l => l != null && Clean(l.Question) != null).ToList();
            if (lines.Count == 0) return Bad<SupplierEvaluationDto>("سوال‌های ارزیابی را وارد کنید.");
            if (lines.Any(l => l.Weight <= 0 || l.Score < 0 || l.Score > 100)) return Bad<SupplierEvaluationDto>("وزن باید مثبت و امتیاز بین ۰ تا ۱۰۰ باشد.");
            List<EvaluationLineDto> applicable = lines.Where(l => l.Score.HasValue).ToList();
            if (applicable.Count == 0) return Bad<SupplierEvaluationDto>("حداقل یک سوال باید امتیاز داشته باشد.");
            // WI-PO-003: final score = sum(S x W) / sum(W); N/A questions are left out
            decimal score = Math.Round(applicable.Sum(l => l.Score.Value * l.Weight) / applicable.Sum(l => l.Weight), 1);
            SupplierGrade grade = SupplierGrades.FromScore(score);
            DateTime date = dto.EvaluationDate == default ? DateTime.Today : dto.EvaluationDate.Date;
            var evaluation = new SupplierEvaluation
            {
                ProviderId = dto.ProviderId, Kind = dto.Kind, EvaluationDate = date, PurchaseOrderId = dto.PurchaseOrderId, Score = score, Grade = grade,
                Notes = Clean(dto.Notes), CreateDate = DateTimeOffset.Now, CreatorUserId = Me, ModifyDate = DateTimeOffset.Now, ModifierUserId = Me,
                Lines = lines.Select(l => new SupplierEvaluationLine { Question = l.Question.Trim(), Weight = l.Weight, Score = l.Score }).ToList(),
            };
            _uow.Set<SupplierEvaluation>().Add(evaluation);
            SupplierProfile profile = await ProfileAsync(dto.ProviderId, cancellationToken);
            profile.Score = score;
            profile.Grade = grade;
            profile.EvaluatedAt = date;
            profile.NextEvaluation = date.AddMonths(6); // periodic re-evaluation every 6 months (WI-PO-003 row 7)
            if (grade == SupplierGrade.D)
            {
                profile.Blocked = true;
                profile.BlockReason = $"گرید D در ارزیابی {date:yyyy-MM-dd} (امتیاز {score})";
            }
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok((await GetEvaluationsAsync(dto.ProviderId, cancellationToken)).Data.First(e => e.Id == evaluation.Id));
        }

        public async Task OnSupplierReturnAsync(int providerId, string documentNumber, CancellationToken cancellationToken)
        {
            SupplierProfile profile = await ProfileAsync(providerId, cancellationToken);
            profile.Warnings++;
            if (profile.Warnings >= 3 && !profile.Blocked)
            {
                profile.Blocked = true;
                profile.BlockReason = $"سومین مرجوعی ({documentNumber}) - قطع همکاری طبق WI-PO-003 تبصره 3؛ رفع آن با مدیرعامل";
            }
        }
    }
}
