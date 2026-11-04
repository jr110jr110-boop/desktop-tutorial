using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Procurement
{
    public interface ISupplierContractService
    {
        Task<ResponseDto<List<AddendumDto>>> GetAddendaAsync(int? purchaseOrderId, CancellationToken cancellationToken);
        Task<ResponseDto<AddendumDto>> SaveAddendumAsync(SaveAddendumDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<AddendumDto>> AddendumActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<GuaranteeDto>>> GetGuaranteesAsync(int? providerId, int? purchaseOrderId, SupplierGuaranteeStatus? status, bool expiringOnly, CancellationToken cancellationToken);
        Task<ResponseDto<GuaranteeDto>> SaveGuaranteeAsync(SaveGuaranteeDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<GuaranteeDto>> GuaranteeActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<GuaranteeAlertResultDto>> RunGuaranteeAlertsAsync(CancellationToken cancellationToken);
        /// <summary>The daily job (no signed-in user): the same alerts, run by the hosted service when Procurement:GuaranteeJob is true.</summary>
        Task<ResponseDto<GuaranteeAlertResultDto>> RunGuaranteeAlertsJobAsync(CancellationToken cancellationToken);

        Task<ResponseDto<List<PaymentTermDto>>> GetPaymentTermsAsync(int purchaseOrderId, CancellationToken cancellationToken);
        Task<ResponseDto<List<PaymentTermDto>>> SavePaymentTermsAsync(SavePaymentTermsDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<List<PaymentTermDto>>> ConfirmTermConditionAsync(ProcurementActionDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PaymentRequestDto>> RequestTermPaymentAsync(RequestTermPaymentDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<ContractPrintDto>> GetContractPrintAsync(int purchaseOrderId, CancellationToken cancellationToken);
    }

    public static class SupplierContractRoles
    {
        public const string Registrars = "COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY";
        public const string GuaranteeReleasers = "FINANCEMANAGER,PMO,CEO";
        public const string Readers = "PMO,CEO,PLANNING,COMMERCIAL,FINANCIAL,FINANCEMANAGER,TREASURY,WAREHOUSE,WHSUPERVISOR,ENGINEERING,QC,AUDITOR";
        public const string DocumentConfirmers = "ENGINEERING,QC";
    }

    /// <summary>
    /// PB6: what happens to a supplier contract after it is released: addenda (volume within the template limit, price, time), the register of the guarantees the supplier gave,
    /// the payment milestones of the template (30 / 30 / 30 / 10) that drive the payment requests, and the printed contract.
    /// </summary>
    public class SupplierContractService : ISupplierContractService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPayablesService _payables;
        private readonly IProcurementService _procurement;
        private readonly global::Services.NasbNiro.Finance.IBudgetService _budget;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public SupplierContractService(IUnitOfWork uow, IInventoryUserContext user, IPayablesService payables, IProcurementService procurement, global::Services.NasbNiro.Office.INotificationService notify, global::Services.NasbNiro.Finance.IBudgetService budget = null)
        {
            _budget = budget;
            _procurement = procurement ?? throw new ArgumentNullException(nameof(procurement));
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _payables = payables ?? throw new ArgumentNullException(nameof(payables));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "این تایید باید توسط شخص دیگری غیر از ثبت‌کننده یا تاییدکننده قبلی انجام شود.";

        // ------------------------------------------------------------------------------------------ addenda

        private async Task<List<AddendumDto>> LoadAddendaAsync(IQueryable<PurchaseOrderAddendum> source, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            List<PurchaseOrderAddendum> rows = await source.Include(a => a.PurchaseOrder).Include(a => a.Lines).ThenInclude(l => l.OrderLine).AsSplitQuery()
                .OrderByDescending(a => a.Id).Take(500).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(a => new int?[] { a.CreatorUserId, a.OrderApprovedByUserId, a.CeoApprovedByUserId }), cancellationToken);
            var result = new List<AddendumDto>();
            foreach (PurchaseOrderAddendum a in rows)
            {
                var dto = new AddendumDto
                {
                    Id = a.Id, Number = a.Number, PurchaseOrderId = a.PurchaseOrderId, PurchaseOrderNumber = a.PurchaseOrder.Number, Sequence = a.Sequence, Kind = a.Kind, Status = a.Status,
                    Reason = a.Reason, AmountBefore = a.AmountBefore, AmountAfter = a.AmountAfter, DeliveryDateBefore = a.DeliveryDateBefore, DeliveryDateAfter = a.DeliveryDateAfter,
                    MaxVolumeChangePercent = a.MaxVolumeChangePercent, VolumeLimitPercent = s[ProcurementSettingKeys.AddendumVolumePercent], NeedsCeo = a.NeedsCeo,
                    CreatorName = names.Name(a.CreatorUserId), CreateDate = a.CreateDate, OrderApprovedAt = a.OrderApprovedAt, OrderApprovedByName = names.Name(a.OrderApprovedByUserId),
                    CeoApprovedAt = a.CeoApprovedAt, CeoApprovedByName = names.Name(a.CeoApprovedByUserId), AppliedAt = a.AppliedAt, RejectReason = a.RejectReason,
                };
                foreach (PurchaseOrderAddendumLine l in a.Lines)
                {
                    decimal original = await OriginalQuantityAsync(l.OrderLine, a.Id, cancellationToken);
                    dto.Lines.Add(new AddendumLineDto
                    {
                        OrderLineId = l.OrderLineId, Description = l.OrderLine.Description, Unit = l.OrderLine.Unit, OriginalQuantity = original, QuantityBefore = l.QuantityBefore,
                        QuantityAfter = l.QuantityAfter, UnitPriceBefore = l.UnitPriceBefore, UnitPriceAfter = l.UnitPriceAfter,
                        VolumeChangePercent = original <= 0 ? 0 : Math.Round(Math.Abs(l.QuantityAfter - original) / original * 100m, 2),
                    });
                }
                result.Add(dto);
            }
            return result;
        }

        /// <summary>The quantity of the line when the contract was signed: the "before" of its first applied addendum, else its quantity now.</summary>
        private async Task<decimal> OriginalQuantityAsync(PurchaseOrderLine line, int exceptAddendumId, CancellationToken cancellationToken)
        {
            decimal? first = await _uow.Set<PurchaseOrderAddendumLine>().Where(l => l.OrderLineId == line.Id && l.Addendum.Status == AddendumStatus.Applied && l.AddendumId != exceptAddendumId)
                .OrderBy(l => l.AddendumId).Select(l => (decimal?)l.QuantityBefore).FirstOrDefaultAsync(cancellationToken);
            return first ?? line.Quantity;
        }

        public async Task<ResponseDto<List<AddendumDto>>> GetAddendaAsync(int? purchaseOrderId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(SupplierContractRoles.Readers)) return Bad<List<AddendumDto>>(NotAllowed);
            IQueryable<PurchaseOrderAddendum> q = _uow.Set<PurchaseOrderAddendum>().Where(a => !a.Deleted);
            if (purchaseOrderId.HasValue) q = q.Where(a => a.PurchaseOrderId == purchaseOrderId.Value);
            return ResponseDtoHelper.Ok(await LoadAddendaAsync(q, cancellationToken));
        }

        public async Task<ResponseDto<AddendumDto>> SaveAddendumAsync(SaveAddendumDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<AddendumDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<AddendumDto>(NotAllowed + " (بازرگانی)");
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<AddendumDto>("علت الحاقیه را بنویسید.");
            PurchaseOrder order = await _uow.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == dto.PurchaseOrderId && !o.Deleted, cancellationToken);
            if (order == null) return ResponseDtoHelper.NotFound<AddendumDto>("سفارش پیدا نشد.");
            if (order.Status != PurchaseOrderStatus.Released && order.Status != PurchaseOrderStatus.PartlyReceived && order.Status != PurchaseOrderStatus.Received)
                return Bad<AddendumDto>("الحاقیه برای قراردادی است که ابلاغ شده و اجرا شروع شده؛ سفارش پیش‌نویس را مستقیم ویرایش کنید.");
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            decimal limit = s[ProcurementSettingKeys.AddendumVolumePercent];

            List<SaveAddendumLineDto> lines = (dto.Lines ?? new List<SaveAddendumLineDto>()).Where(l => l != null).ToList();
            if (lines.Select(l => l.OrderLineId).Distinct().Count() != lines.Count) return Bad<AddendumDto>("یک ردیف بیش از یک بار آمده است.");
            decimal maxVolume = 0;
            var kept = new List<PurchaseOrderAddendumLine>();
            foreach (SaveAddendumLineDto l in lines)
            {
                PurchaseOrderLine line = order.Lines.FirstOrDefault(x => x.Id == l.OrderLineId);
                if (line == null) return Bad<AddendumDto>("ردیف الحاقیه متعلق به این سفارش نیست.");
                if (l.QuantityAfter < 0 || l.UnitPriceAfter < 0) return Bad<AddendumDto>($"ردیف {line.RowNumber}: مقدار و قیمت نمی‌تواند منفی باشد.");
                if (l.QuantityAfter == line.Quantity && l.UnitPriceAfter == line.UnitPrice) continue;
                if (l.QuantityAfter < line.ReceivedQuantity || l.QuantityAfter < line.InvoicedQuantity)
                    return Bad<AddendumDto>($"ردیف {line.RowNumber}: مقدار جدید از مقدار تحویل‌شده یا فاکتورشده کمتر نمی‌شود.");
                decimal original = await OriginalQuantityAsync(line, dto.Id ?? 0, cancellationToken);
                decimal change = original <= 0 ? 0 : Math.Abs(l.QuantityAfter - original) / original * 100m;
                if (change > limit) return Bad<AddendumDto>($"ردیف {line.RowNumber}: تغییر حجم {change:0.##} درصد است و از سقف {limit:0.##} درصد قرارداد (جمعاً نسبت به مقدار اولیه) بیشتر است؛ سفارش تازه بدهید.");
                decimal originalPrice = await _uow.Set<PurchaseOrderAddendumLine>().Where(x => x.OrderLineId == line.Id && x.Addendum.Status == AddendumStatus.Applied && x.AddendumId != (dto.Id ?? 0))
                    .OrderBy(x => x.AddendumId).Select(x => (decimal?)x.UnitPriceBefore).FirstOrDefaultAsync(cancellationToken) ?? line.UnitPrice;
                decimal priceChange = originalPrice <= 0 ? 0 : Math.Abs(l.UnitPriceAfter - originalPrice) / originalPrice * 100m;
                if (priceChange > limit) return Bad<AddendumDto>($"ردیف {line.RowNumber}: تغییر قیمت {priceChange:0.##} درصد است و از سقف {limit:0.##} درصد بیشتر است؛ سفارش تازه بدهید.");
                maxVolume = Math.Max(maxVolume, change);
                kept.Add(new PurchaseOrderAddendumLine
                {
                    OrderLineId = line.Id, QuantityBefore = line.Quantity, QuantityAfter = l.QuantityAfter, UnitPriceBefore = line.UnitPrice, UnitPriceAfter = l.UnitPriceAfter,
                });
            }
            DateTime? newDate = dto.DeliveryDateAfter?.Date;
            bool timeChange = newDate.HasValue && newDate != order.DeliveryDate?.Date;
            if (kept.Count == 0 && !timeChange) return Bad<AddendumDto>("هیچ تغییری در حجم، قیمت یا زمان نیست.");
            bool volume = kept.Any(l => l.QuantityAfter != l.QuantityBefore), price = kept.Any(l => l.UnitPriceAfter != l.UnitPriceBefore);
            int kinds = (volume ? 1 : 0) + (price ? 1 : 0) + (timeChange ? 1 : 0);
            AddendumKind kind = kinds > 1 ? AddendumKind.Mixed : volume ? AddendumKind.Volume : price ? AddendumKind.Price : AddendumKind.Time;
            decimal after = Math.Round(order.Lines.Sum(x =>
            {
                PurchaseOrderAddendumLine c = kept.FirstOrDefault(k => k.OrderLineId == x.Id);
                return c == null ? x.Quantity * x.UnitPrice : c.QuantityAfter * c.UnitPriceAfter;
            }), 0);

            PurchaseOrderAddendum addendum;
            if (dto.Id.HasValue)
            {
                addendum = await _uow.Set<PurchaseOrderAddendum>().Include(a => a.Lines).FirstOrDefaultAsync(a => a.Id == dto.Id.Value && !a.Deleted, cancellationToken);
                if (addendum == null) return ResponseDtoHelper.NotFound<AddendumDto>("الحاقیه پیدا نشد.");
                if (addendum.Status != AddendumStatus.Draft) return Bad<AddendumDto>("فقط پیش‌نویس ویرایش می‌شود.");
                if (addendum.CreatorUserId != Me) return Bad<AddendumDto>(NotAllowed);
                if (addendum.PurchaseOrderId != order.Id) return Bad<AddendumDto>("این الحاقیه متعلق به سفارش دیگری است.");
                _uow.Set<PurchaseOrderAddendumLine>().RemoveRange(addendum.Lines);
                addendum.Lines = new List<PurchaseOrderAddendumLine>();
            }
            else
            {
                if (await _uow.Set<PurchaseOrderAddendum>().AnyAsync(a => a.PurchaseOrderId == order.Id && (a.Status == AddendumStatus.Draft || a.Status == AddendumStatus.Submitted) && !a.Deleted, cancellationToken))
                    return Bad<AddendumDto>("برای این سفارش الحاقیه باز (در دست تنظیم یا تایید) وجود دارد.");
                int sequence = await _uow.Set<PurchaseOrderAddendum>().Where(a => a.PurchaseOrderId == order.Id).CountAsync(cancellationToken) + 1;
                addendum = new PurchaseOrderAddendum
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.Addendum, "ADD", DateTime.Today, cancellationToken),
                    PurchaseOrderId = order.Id, Sequence = sequence, Status = AddendumStatus.Draft, CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Lines = new List<PurchaseOrderAddendumLine>(),
                };
                _uow.Set<PurchaseOrderAddendum>().Add(addendum);
            }
            addendum.Kind = kind; addendum.Reason = reason;
            addendum.AmountBefore = order.TotalAmount; addendum.AmountAfter = after;
            addendum.DeliveryDateBefore = order.DeliveryDate; addendum.DeliveryDateAfter = timeChange ? newDate : null;
            addendum.MaxVolumeChangePercent = Math.Round(maxVolume, 2);
            // PB6 (M5): the CEO threshold is judged on the cumulative change against the amount first signed, so small steps cannot dodge it
            decimal? firstBefore = await _uow.Set<PurchaseOrderAddendum>().Where(a => a.PurchaseOrderId == order.Id && a.Status == AddendumStatus.Applied && !a.Deleted && a.Id != addendum.Id)
                .OrderBy(a => a.Id).Select(a => (decimal?)a.AmountBefore).FirstOrDefaultAsync(cancellationToken);
            decimal signedAmount = firstBefore ?? order.TotalAmount;
            addendum.NeedsCeo = Math.Abs(after - signedAmount) >= s[ProcurementSettingKeys.AddendumCeoFrom] || Math.Abs(after - order.TotalAmount) >= s[ProcurementSettingKeys.AddendumCeoFrom];
            foreach (PurchaseOrderAddendumLine l in kept) addendum.Lines.Add(l);
            addendum.ModifyDate = DateTimeOffset.Now; addendum.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetAddendumAsync(addendum.Id, cancellationToken);
        }

        private async Task<ResponseDto<AddendumDto>> GetAddendumAsync(int id, CancellationToken cancellationToken)
        {
            List<AddendumDto> list = await LoadAddendaAsync(_uow.Set<PurchaseOrderAddendum>().Where(a => a.Id == id), cancellationToken);
            return list.Count == 0 ? ResponseDtoHelper.NotFound<AddendumDto>("الحاقیه پیدا نشد.") : ResponseDtoHelper.Ok(list[0]);
        }

        public async Task<ResponseDto<AddendumDto>> AddendumActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            PurchaseOrderAddendum a = await _uow.Set<PurchaseOrderAddendum>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (a == null) return ResponseDtoHelper.NotFound<AddendumDto>("الحاقیه پیدا نشد.");
            PurchaseOrder order = await _uow.Set<PurchaseOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == a.PurchaseOrderId, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            string note = Clean(dto.Note);
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (a.Status != AddendumStatus.Draft) return Bad<AddendumDto>("الحاقیه قبلاً ارسال شده است.");
                    if (a.CreatorUserId != Me) return Bad<AddendumDto>(NotAllowed);
                    if (a.AmountBefore != order.TotalAmount) return Bad<AddendumDto>("مبلغ سفارش از زمان تنظیم تغییر کرده؛ الحاقیه را دوباره ذخیره کنید.");
                    a.Status = AddendumStatus.Submitted; a.SubmittedAt = now;
                    break;
                case "approve":
                    if (a.Status != AddendumStatus.Submitted || a.OrderApprovedAt.HasValue) return Bad<AddendumDto>("الحاقیه در مرحله تایید سفارش نیست.");
                    if (!_user.IsInRole(ProcurementRoles.OrderApproval)) return Bad<AddendumDto>(NotAllowed + " (معاونت PMO)");
                    if (a.CreatorUserId == Me) return Bad<AddendumDto>(SameUser);
                    a.OrderApprovedAt = now; a.OrderApprovedByUserId = Me;
                    break;
                case "approve-ceo":
                    if (a.Status != AddendumStatus.Submitted || !a.OrderApprovedAt.HasValue || !a.NeedsCeo || a.CeoApprovedAt.HasValue) return Bad<AddendumDto>("الحاقیه در مرحله تایید مدیرعامل نیست.");
                    if (!_user.IsInRole("CEO")) return Bad<AddendumDto>(NotAllowed + " (مدیرعامل)");
                    if (a.CreatorUserId == Me || a.OrderApprovedByUserId == Me) return Bad<AddendumDto>(SameUser);
                    a.CeoApprovedAt = now; a.CeoApprovedByUserId = Me;
                    break;
                case "reject":
                    if (a.Status != AddendumStatus.Submitted) return Bad<AddendumDto>("فقط الحاقیه ارسال‌شده رد می‌شود.");
                    if (!_user.IsInRole(ProcurementRoles.OrderApproval)) return Bad<AddendumDto>(NotAllowed);
                    if (a.CreatorUserId == Me) return Bad<AddendumDto>(SameUser);
                    if (note == null) return Bad<AddendumDto>("علت رد را بنویسید.");
                    a.Status = AddendumStatus.Rejected; a.RejectedAt = now; a.RejectedByUserId = Me; a.RejectReason = note;
                    break;
                default:
                    return Bad<AddendumDto>("عمل نامعتبر است.");
            }
            if (a.Status == AddendumStatus.Submitted && a.OrderApprovedAt.HasValue && (!a.NeedsCeo || a.CeoApprovedAt.HasValue))
            {
                string applyError = await ApplyAsync(a, order, now, cancellationToken);
                if (applyError != null) return Bad<AddendumDto>(applyError);
            }
            a.ModifyDate = now; a.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<AddendumDto>("اطلاعات هم‌زمان تغییر کرد؛ دوباره تلاش کنید."); }
            bool waitingOrder = a.Status == AddendumStatus.Submitted && !a.OrderApprovedAt.HasValue;
            bool waitingCeo = a.Status == AddendumStatus.Submitted && a.OrderApprovedAt.HasValue && a.NeedsCeo && !a.CeoApprovedAt.HasValue;
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "add:" + a.Id, waitingOrder || waitingCeo, "procurement", "addendum", a.Id,
                "تایید الحاقیه " + a.Number, "/procurement/contracts/" + a.PurchaseOrderId, waitingCeo ? "CEO" : ProcurementRoles.OrderApproval, cancellationToken);
            if (a.Status == AddendumStatus.Applied)
                await ProcOrderNotices.SendAsync(_uow, _notify, Me, a.PurchaseOrderId, "addendum:" + a.Id, "الحاقیه " + a.Number + " روی قرارداد اعمال شد", cancellationToken);
            return await GetAddendumAsync(a.Id, cancellationToken);
        }

        private async Task<string> ApplyAsync(PurchaseOrderAddendum a, PurchaseOrder order, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (order.Status == PurchaseOrderStatus.Closed || order.Status == PurchaseOrderStatus.Cancelled) return "سفارش بسته یا باطل شده است.";
            foreach (PurchaseOrderAddendumLine l in a.Lines)
            {
                PurchaseOrderLine line = order.Lines.FirstOrDefault(x => x.Id == l.OrderLineId);
                if (line == null) return "ردیف سفارش پیدا نشد.";
                if (line.Quantity != l.QuantityBefore || line.UnitPrice != l.UnitPriceBefore) return "ردیف سفارش از زمان تنظیم الحاقیه تغییر کرده است؛ الحاقیه تازه بنویسید.";
                if (l.QuantityAfter < line.ReceivedQuantity || l.QuantityAfter < line.InvoicedQuantity) return "مقدار جدید از مقدار تحویل‌شده یا فاکتورشده کمتر است.";
                line.Quantity = l.QuantityAfter; line.UnitPrice = l.UnitPriceAfter;
            }
            // PB6 (M5): the order as it stood is frozen before the change (append only, with a hash)
            string snapshot = OrderSnapshot.Json(order);
            _uow.Set<PurchaseOrderRevision>().Add(new PurchaseOrderRevision
            {
                PurchaseOrderId = order.Id, Revision = order.Revision, Reason = "الحاقیه " + a.Number + ": " + a.Reason, StatusWhenFrozen = order.Status, TotalAmount = order.TotalAmount,
                SnapshotJson = snapshot, Hash = global::Services.NasbNiro.ProjectControl.PcText.Sha256(snapshot), FrozenAt = now, FrozenByUserId = Me,
                ApprovedAt = order.ApprovedAt, ApprovedByUserId = order.ApprovedByUserId,
            });
            order.Revision++;
            if (a.DeliveryDateAfter.HasValue) order.DeliveryDate = a.DeliveryDateAfter;
            decimal oldTotal = order.TotalAmount;
            order.TotalAmount = Math.Round(order.Lines.Sum(x => x.Quantity * x.UnitPrice), 0);
            // an increase is a new commitment: the budget gate sees it as the order's value change (the CEO override is the budget gate's own rule)
            if (order.TotalAmount > oldTotal && _budget != null)
            {
                string center = await _uow.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == order.Id && l.RequestLine != null).Select(l => l.RequestLine.PurchaseRequest.CostCenter).FirstOrDefaultAsync(cancellationToken);
                int? requestId = await _uow.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == order.Id && l.RequestLine != null).Select(l => (int?)l.RequestLine.PurchaseRequestId).FirstOrDefaultAsync(cancellationToken);
                var gate = await _budget.GateAsync(center, order.ContractId, order.TotalAmount, order.OrderDate, requestId, order.Id, a.Reason, cancellationToken);
                if (!gate.Allowed) return gate.Message;
            }
            // the milestones that are still waiting follow the new value; the last one takes the rounding remainder so the terms still add up to the order
            List<PurchaseOrderPaymentTerm> terms = await _uow.Set<PurchaseOrderPaymentTerm>().Where(t => t.PurchaseOrderId == order.Id).OrderBy(t => t.Id).ToListAsync(cancellationToken);
            List<PurchaseOrderPaymentTerm> pending = terms.Where(t => t.Status == PaymentTermStatus.Pending).ToList();
            foreach (PurchaseOrderPaymentTerm t in pending) t.Amount = Math.Round(order.TotalAmount * t.Percent / 100m, 0);
            if (pending.Count > 0 && terms.Sum(t => t.Percent) == 100m)
                pending[pending.Count - 1].Amount += order.TotalAmount - terms.Sum(t => t.Amount);
            a.Status = AddendumStatus.Applied; a.AppliedAt = now;
            order.ModifyDate = now; order.ModifierUserId = Me;
            return null;
        }

        // ------------------------------------------------------------------------------------------ guarantees

        private GuaranteeDto ToDto(SupplierGuarantee g, Dictionary<int, string> names, int alertDays, Dictionary<int, int> files)
        {
            int days = (int)(g.ExpiryDate.Date - DateTime.Today).TotalDays;
            bool open = g.Status == SupplierGuaranteeStatus.Active || g.Status == SupplierGuaranteeStatus.ReleaseRequested;
            return new GuaranteeDto
            {
                Id = g.Id, Number = g.Number, ProviderId = g.ProviderId, ProviderName = g.Provider?.Name, PurchaseOrderId = g.PurchaseOrderId, PurchaseOrderNumber = g.PurchaseOrder?.Number,
                Kind = g.Kind, Purpose = g.Purpose, Amount = g.Amount, GuaranteeNumber = g.GuaranteeNumber, Issuer = g.Issuer, IssueDate = g.IssueDate, ExpiryDate = g.ExpiryDate, Status = g.Status,
                ReleaseConditions = g.ReleaseConditions, ReleaseReason = g.ReleaseReason, ReleaseRequestedAt = g.ReleaseRequestedAt, ReleaseRequestedByName = names.Name(g.ReleaseRequestedByUserId),
                ReleasedAt = g.ReleasedAt, ReleasedByName = names.Name(g.ReleasedByUserId), DaysToExpiry = days,
                ExpiryState = !open ? "none" : days < 0 ? "expired" : days <= alertDays ? "soon" : "none",
                AttachmentCount = files.TryGetValue(g.Id, out int c) ? c : 0,
            };
        }

        public async Task<ResponseDto<List<GuaranteeDto>>> GetGuaranteesAsync(int? providerId, int? purchaseOrderId, SupplierGuaranteeStatus? status, bool expiringOnly, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(SupplierContractRoles.Readers)) return Bad<List<GuaranteeDto>>(NotAllowed);
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            int alertDays = (int)s[ProcurementSettingKeys.GuaranteeAlertDays];
            IQueryable<SupplierGuarantee> q = _uow.Set<SupplierGuarantee>().Include(g => g.Provider).Include(g => g.PurchaseOrder).Where(g => !g.Deleted);
            if (providerId.HasValue) q = q.Where(g => g.ProviderId == providerId.Value);
            if (purchaseOrderId.HasValue) q = q.Where(g => g.PurchaseOrderId == purchaseOrderId.Value);
            if (status.HasValue) q = q.Where(g => g.Status == status.Value);
            if (expiringOnly)
            {
                DateTime limit = DateTime.Today.AddDays(alertDays);
                q = q.Where(g => (g.Status == SupplierGuaranteeStatus.Active || g.Status == SupplierGuaranteeStatus.ReleaseRequested) && g.ExpiryDate <= limit);
            }
            List<SupplierGuarantee> rows = await q.OrderBy(g => g.ExpiryDate).Take(1000).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(g => new int?[] { g.ReleaseRequestedByUserId, g.ReleasedByUserId }), cancellationToken);
            List<int> ids = rows.Select(g => g.Id).ToList();
            var ownerType = global::Entities.Model.NasbNiro.Attachments.AttachmentOwnerType.SupplierGuarantee;
            var counts = await _uow.Set<global::Entities.Model.NasbNiro.Attachments.RecordAttachment>().Where(a => a.OwnerType == ownerType && ids.Contains(a.OwnerId) && !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null)
                .GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            return ResponseDtoHelper.Ok(rows.Select(g => ToDto(g, names, alertDays, counts.ToDictionary(c => c.Key, c => c.Count))).ToList());
        }

        private async Task<ResponseDto<GuaranteeDto>> GetGuaranteeAsync(int id, CancellationToken cancellationToken)
        {
            ResponseDto<List<GuaranteeDto>> all = await GetGuaranteesAsync(null, null, null, false, cancellationToken);
            GuaranteeDto one = all.Data?.FirstOrDefault(g => g.Id == id);
            return one == null ? ResponseDtoHelper.NotFound<GuaranteeDto>("ضمانت‌نامه پیدا نشد.") : ResponseDtoHelper.Ok(one);
        }

        public async Task<ResponseDto<GuaranteeDto>> SaveGuaranteeAsync(SaveGuaranteeDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<GuaranteeDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(SupplierContractRoles.Registrars)) return Bad<GuaranteeDto>(NotAllowed);
            if (!Enum.IsDefined(typeof(GuaranteeKind), dto.Kind) || !Enum.IsDefined(typeof(GuaranteePurpose), dto.Purpose)) return Bad<GuaranteeDto>("نوع یا هدف ضمانت‌نامه نامعتبر است.");
            if (dto.Amount <= 0) return Bad<GuaranteeDto>("مبلغ باید بیشتر از صفر باشد.");
            if (Clean(dto.GuaranteeNumber) == null) return Bad<GuaranteeDto>("شماره چک یا ضمانت‌نامه را بنویسید.");
            if (Clean(dto.Issuer) == null) return Bad<GuaranteeDto>("صادرکننده (بانک یا صاحب چک) را بنویسید.");
            if (dto.ExpiryDate.Date < dto.IssueDate.Date) return Bad<GuaranteeDto>("تاریخ پایان اعتبار از تاریخ صدور زودتر است.");
            if (!await _uow.Set<global::Entities.Model.NasbNiro.Providers.Provider>().AnyAsync(p => p.Id == dto.ProviderId && !p.Deleted, cancellationToken)) return Bad<GuaranteeDto>("تامین‌کننده پیدا نشد.");
            if (dto.PurchaseOrderId.HasValue && !await _uow.Set<PurchaseOrder>().AnyAsync(o => o.Id == dto.PurchaseOrderId.Value && o.ProviderId == dto.ProviderId && !o.Deleted, cancellationToken))
                return Bad<GuaranteeDto>("سفارش پیدا نشد یا متعلق به این تامین‌کننده نیست.");
            SupplierGuarantee g;
            if (dto.Id.HasValue)
            {
                g = await _uow.Set<SupplierGuarantee>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (g == null) return ResponseDtoHelper.NotFound<GuaranteeDto>("ضمانت‌نامه پیدا نشد.");
                if (g.Status != SupplierGuaranteeStatus.Active) return Bad<GuaranteeDto>("فقط ضمانت‌نامه فعال ویرایش می‌شود.");
            }
            else
            {
                g = new SupplierGuarantee
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.Guarantee, "GRN", DateTime.Today, cancellationToken),
                    Status = SupplierGuaranteeStatus.Active, CreateDate = DateTimeOffset.Now, CreatorUserId = Me,
                };
                _uow.Set<SupplierGuarantee>().Add(g);
            }
            g.ProviderId = dto.ProviderId; g.PurchaseOrderId = dto.PurchaseOrderId; g.Kind = dto.Kind; g.Purpose = dto.Purpose; g.Amount = dto.Amount;
            g.GuaranteeNumber = Clean(dto.GuaranteeNumber); g.Issuer = Clean(dto.Issuer); g.IssueDate = dto.IssueDate.Date; g.ExpiryDate = dto.ExpiryDate.Date;
            g.ReleaseConditions = Clean(dto.ReleaseConditions);
            g.ModifyDate = DateTimeOffset.Now; g.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetGuaranteeAsync(g.Id, cancellationToken);
        }

        public async Task<ResponseDto<GuaranteeDto>> GuaranteeActionAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            SupplierGuarantee g = await _uow.Set<SupplierGuarantee>().Include(x => x.PurchaseOrder).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (g == null) return ResponseDtoHelper.NotFound<GuaranteeDto>("ضمانت‌نامه پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            string note = Clean(dto.Note);
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "request-release":
                    if (!_user.IsInRole(SupplierContractRoles.Registrars)) return Bad<GuaranteeDto>(NotAllowed);
                    if (g.Status != SupplierGuaranteeStatus.Active) return Bad<GuaranteeDto>("فقط ضمانت‌نامه فعال برای آزادسازی درخواست می‌شود.");
                    if (note == null) return Bad<GuaranteeDto>("علت و شرط آزادسازی را بنویسید.");
                    // a guarantee of an order is released only when its obligation is over: the order is received or closed
                    if (g.PurchaseOrder != null && g.Purpose != GuaranteePurpose.Advance && g.PurchaseOrder.Status != PurchaseOrderStatus.Received && g.PurchaseOrder.Status != PurchaseOrderStatus.Closed)
                        return Bad<GuaranteeDto>("ضمانت‌نامه حسن انجام کار پس از دریافت کامل یا بسته شدن سفارش آزاد می‌شود.");
                    g.Status = SupplierGuaranteeStatus.ReleaseRequested; g.ReleaseRequestedAt = now; g.ReleaseRequestedByUserId = Me; g.ReleaseReason = note;
                    break;
                case "release":
                    if (g.Status != SupplierGuaranteeStatus.ReleaseRequested) return Bad<GuaranteeDto>("ابتدا درخواست آزادسازی ثبت شود.");
                    if (!_user.IsInRole(SupplierContractRoles.GuaranteeReleasers)) return Bad<GuaranteeDto>(NotAllowed + " (مدیر مالی یا معاونت PMO)");
                    if (g.ReleaseRequestedByUserId == Me || g.CreatorUserId == Me) return Bad<GuaranteeDto>(SameUser);
                    g.Status = SupplierGuaranteeStatus.Released; g.ReleasedAt = now; g.ReleasedByUserId = Me;
                    break;
                case "reject-release":
                    if (g.Status != SupplierGuaranteeStatus.ReleaseRequested) return Bad<GuaranteeDto>("درخواست آزادسازی باز نیست.");
                    if (!_user.IsInRole(SupplierContractRoles.GuaranteeReleasers)) return Bad<GuaranteeDto>(NotAllowed);
                    if (g.ReleaseRequestedByUserId == Me) return Bad<GuaranteeDto>(SameUser);
                    if (note == null) return Bad<GuaranteeDto>("علت رد را بنویسید.");
                    g.Status = SupplierGuaranteeStatus.Active; g.ReleaseRequestedAt = null; g.ReleaseRequestedByUserId = null; g.ReleaseReason = null;
                    break;
                case "claim":
                    if (g.Status != SupplierGuaranteeStatus.Active && g.Status != SupplierGuaranteeStatus.Expired) return Bad<GuaranteeDto>("این ضمانت‌نامه قابل مصادره نیست.");
                    if (!_user.IsInRole("CEO")) return Bad<GuaranteeDto>(NotAllowed + " (مدیرعامل)");
                    if (note == null) return Bad<GuaranteeDto>("علت مصادره را بنویسید.");
                    g.Status = SupplierGuaranteeStatus.Claimed; g.ReleaseReason = note;
                    break;
                default:
                    return Bad<GuaranteeDto>("عمل نامعتبر است.");
            }
            g.ModifyDate = now; g.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "grel:" + g.Id, g.Status == SupplierGuaranteeStatus.ReleaseRequested, "procurement", "guarantee", g.Id,
                "تایید آزادسازی ضمانت‌نامه " + g.Number, "/procurement/guarantees?id=" + g.Id, SupplierContractRoles.GuaranteeReleasers, cancellationToken);
            return await GetGuaranteeAsync(g.Id, cancellationToken);
        }

        /// <summary>Marks the guarantees whose date has passed as expired and puts the ones that are about to expire in the inbox of the buyer and finance. Safe to call often.</summary>
        public async Task<ResponseDto<GuaranteeAlertResultDto>> RunGuaranteeAlertsAsync(CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(SupplierContractRoles.Readers)) return Bad<GuaranteeAlertResultDto>(NotAllowed);
            return await RunGuaranteeAlertsJobAsync(cancellationToken);
        }

        public async Task<ResponseDto<GuaranteeAlertResultDto>> RunGuaranteeAlertsJobAsync(CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            DateTime limit = DateTime.Today.AddDays((int)s[ProcurementSettingKeys.GuaranteeAlertDays]);
            List<SupplierGuarantee> open = await _uow.Set<SupplierGuarantee>().Where(g => !g.Deleted && g.Status == SupplierGuaranteeStatus.Active && g.ExpiryDate <= limit).ToListAsync(cancellationToken);
            var result = new GuaranteeAlertResultDto();
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (SupplierGuarantee g in open)
            {
                bool expired = g.ExpiryDate.Date < DateTime.Today;
                if (expired) { g.Status = SupplierGuaranteeStatus.Expired; result.MarkedExpired++; }
                g.LastAlertAt = now;
                result.Alerted++;
            }
            if (open.Count > 0) await _uow.SaveChangesAsync(cancellationToken);
            foreach (SupplierGuarantee g in open)
                await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "gexp:" + g.Id, true, "procurement", "guarantee", g.Id,
                    (g.Status == SupplierGuaranteeStatus.Expired ? "ضمانت‌نامه منقضی شد: " : "نزدیک پایان اعتبار: ") + g.Number, "/procurement/guarantees?id=" + g.Id,
                    "COMMERCIAL,FINANCIAL", cancellationToken, g.ExpiryDate, Entities.Model.NasbNiro.Office.OfInboxKind.Alert);
            return ResponseDtoHelper.Ok(result);
        }

        // ------------------------------------------------------------------------------------------ payment terms

        private static string TermTitle(PaymentTermKind kind) => kind switch
        {
            PaymentTermKind.Advance => "پیش‌پرداخت",
            PaymentTermKind.AfterDeliveryQc => "پس از تحویل و تایید کنترل کیفی",
            PaymentTermKind.ChequeAfterInvoice => "چک پس از تحویل فاکتور",
            PaymentTermKind.RetentionAfterDocuments => "مابقی پس از تحویل مدارک بازرسی و نقشه‌ها",
            _ => "سایر",
        };

        public async Task<ResponseDto<List<PaymentTermDto>>> GetPaymentTermsAsync(int purchaseOrderId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(SupplierContractRoles.Readers)) return Bad<List<PaymentTermDto>>(NotAllowed);
            PurchaseOrder order = await _uow.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == purchaseOrderId && !o.Deleted, cancellationToken);
            if (order == null) return ResponseDtoHelper.NotFound<List<PaymentTermDto>>("سفارش پیدا نشد.");
            return ResponseDtoHelper.Ok(await BuildTermsAsync(order, cancellationToken));
        }

        private async Task<List<PaymentTermDto>> BuildTermsAsync(PurchaseOrder order, CancellationToken cancellationToken)
        {
            List<PurchaseOrderPaymentTerm> terms = await _uow.Set<PurchaseOrderPaymentTerm>().Where(t => t.PurchaseOrderId == order.Id).OrderBy(t => t.RowNumber).ToListAsync(cancellationToken);
            List<int> requestIds = terms.Where(t => t.PaymentRequestId.HasValue).Select(t => t.PaymentRequestId.Value).ToList();
            var requests = await _uow.Set<PaymentRequest>().Where(p => requestIds.Contains(p.Id)).Select(p => new { p.Id, p.Number, p.Status }).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, terms.Select(t => t.ConditionConfirmedByUserId), cancellationToken);
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);

            Dictionary<int, decimal> accepted = new Dictionary<int, decimal>();
            if (terms.Any(t => t.Kind == PaymentTermKind.AfterDeliveryQc || t.Kind == PaymentTermKind.RetentionAfterDocuments))
            {
                // the same rule the warehouse and the invoice use: QC-accepted receipts, receipts without inspection, accepted services
                var lineIds = order.Lines.Select(l => l.Id).ToList();
                accepted = await AcceptedAsync(lineIds, cancellationToken);
            }
            SupplierInvoice invoice = await _uow.Set<SupplierInvoice>().Where(i => i.PurchaseOrderId == order.Id && i.Status == SupplierInvoiceStatus.Approved && !i.Deleted)
                .OrderByDescending(i => i.InvoiceDate).FirstOrDefaultAsync(cancellationToken);
            var advanceGuarantees = await _uow.Set<SupplierGuarantee>().AsNoTracking().Where(g => g.PurchaseOrderId == order.Id && g.Purpose == GuaranteePurpose.Advance && g.Status == SupplierGuaranteeStatus.Active && !g.Deleted).Select(g => new { g.Amount, g.ExpiryDate }).ToListAsync(cancellationToken);
            bool released = order.Status >= PurchaseOrderStatus.Released && order.Status != PurchaseOrderStatus.Cancelled;

            var list = new List<PaymentTermDto>();
            foreach (PurchaseOrderPaymentTerm t in terms)
            {
                var requested = requests.FirstOrDefault(r => r.Id == t.PaymentRequestId);
                bool eligible = false; string why = null; DateTime? due = null;
                bool reopened = requested != null && requested.Status == PaymentRequestStatus.Rejected;
                if (t.Status != PaymentTermStatus.Pending && !reopened) why = "درخواست پرداخت این مرحله ثبت شده است.";
                else if (!released) why = "سفارش هنوز ابلاغ نشده است.";
                else switch (t.Kind)
                {
                    case PaymentTermKind.Advance:
                        // M6: a guarantee must be active, not past its date and cover the advance; an order that names no guarantee waives it
                        eligible = advanceGuarantees.Any(g => g.ExpiryDate.Date >= DateTime.Today && g.Amount >= t.Amount) || (advanceGuarantees.Count == 0 && string.IsNullOrWhiteSpace(order.AdvanceGuarantee));
                        if (!eligible) why = advanceGuarantees.Count == 0 ? "برای پیش‌پرداخت ضمانت‌نامه فعال ثبت شود." : "ضمانت‌نامه پیش‌پرداخت منقضی است یا مبلغ آن پیش‌پرداخت را نمی‌پوشاند.";
                        break;
                    case PaymentTermKind.AfterDeliveryQc:
                        decimal ordered = order.Lines.Sum(l => l.Quantity);
                        decimal ok = order.Lines.Sum(l => Math.Min(l.Quantity, accepted.TryGetValue(l.Id, out decimal a) ? a : 0));
                        eligible = ordered > 0 && ok >= ordered;
                        if (!eligible) why = "همه ردیف‌ها هنوز تحویل و تایید کنترل کیفی نشده است.";
                        break;
                    case PaymentTermKind.ChequeAfterInvoice:
                        eligible = invoice != null;
                        if (!eligible) why = "فاکتور تاییدشده تامین‌کننده لازم است.";
                        else due = invoice.InvoiceDate.AddDays(t.DueDays ?? (int)s[ProcurementSettingKeys.ChequeAfterInvoiceDays]);
                        break;
                    case PaymentTermKind.RetentionAfterDocuments:
                        eligible = t.ConditionConfirmedAt.HasValue;
                        if (!eligible) why = "تحویل مدارک بازرسی و نقشه‌ها هنوز تایید نشده است.";
                        else
                        {
                            bool allAccepted = order.Lines.Count > 0 && order.Lines.All(l => (accepted.TryGetValue(l.Id, out decimal q) ? q : 0) >= l.Quantity);
                            eligible = allAccepted;
                            if (!eligible) why = "پذیرش نهایی همه ردیف‌ها هنوز انجام نشده است.";
                        }
                        break;
                    default:
                        eligible = true;
                        break;
                }
                list.Add(new PaymentTermDto
                {
                    Id = t.Id, PurchaseOrderId = t.PurchaseOrderId, RowNumber = t.RowNumber, Kind = t.Kind, Percent = t.Percent, Amount = t.Amount, DueDays = t.DueDays, Condition = t.Condition ?? TermTitle(t.Kind),
                    Status = requested != null && requested.Status == PaymentRequestStatus.Paid ? PaymentTermStatus.Paid : reopened ? PaymentTermStatus.Pending : t.Status, PaymentRequestId = t.PaymentRequestId, PaymentRequestNumber = requested?.Number,
                    Eligible = eligible, EligibleNote = eligible ? null : why, DueDate = due, ConditionConfirmedAt = t.ConditionConfirmedAt, ConditionConfirmedByName = names.Name(t.ConditionConfirmedByUserId),
                });
            }
            return list;
        }

        private Task<Dictionary<int, decimal>> AcceptedAsync(List<int> lineIds, CancellationToken cancellationToken) => _procurement.AcceptedQuantitiesAsync(lineIds, cancellationToken);

        public async Task<ResponseDto<List<PaymentTermDto>>> SavePaymentTermsAsync(SavePaymentTermsDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<List<PaymentTermDto>>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<List<PaymentTermDto>>(NotAllowed + " (بازرگانی)");
            PurchaseOrder order = await _uow.Set<PurchaseOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == dto.PurchaseOrderId && !o.Deleted, cancellationToken);
            if (order == null) return ResponseDtoHelper.NotFound<List<PaymentTermDto>>("سفارش پیدا نشد.");
            if (order.Status == PurchaseOrderStatus.Closed || order.Status == PurchaseOrderStatus.Cancelled) return Bad<List<PaymentTermDto>>("سفارش بسته یا باطل شده است.");
            List<PurchaseOrderPaymentTerm> existing = await _uow.Set<PurchaseOrderPaymentTerm>().Where(t => t.PurchaseOrderId == order.Id).ToListAsync(cancellationToken);
            if (existing.Any(t => t.Status != PaymentTermStatus.Pending)) return Bad<List<PaymentTermDto>>("برای یکی از مراحل درخواست پرداخت ثبت شده؛ جدول تغییر نمی‌کند.");
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            List<SavePaymentTermDto> input;
            if (dto.UseTemplate)
                input = new List<SavePaymentTermDto>
                {
                    new SavePaymentTermDto { Kind = PaymentTermKind.Advance, Percent = 30, Condition = "پیش‌پرداخت قرارداد" },
                    new SavePaymentTermDto { Kind = PaymentTermKind.AfterDeliveryQc, Percent = 30, Condition = "پس از تحویل و تایید همه آیتم‌ها توسط کنترل کیفی خریدار" },
                    new SavePaymentTermDto { Kind = PaymentTermKind.ChequeAfterInvoice, Percent = 30, DueDays = (int)s[ProcurementSettingKeys.ChequeAfterInvoiceDays], Condition = "چک با سررسید پس از تحویل فاکتور تاییدشده" },
                    new SavePaymentTermDto { Kind = PaymentTermKind.RetentionAfterDocuments, Percent = 10, Condition = "پس از تحویل مدارک بازرسی و اصل نقشه‌ها" },
                };
            else input = (dto.Terms ?? new List<SavePaymentTermDto>()).Where(t => t != null).ToList();
            if (input.Count == 0) return Bad<List<PaymentTermDto>>("حداقل یک مرحله لازم است.");
            if (input.Any(t => !Enum.IsDefined(typeof(PaymentTermKind), t.Kind) || t.Percent <= 0 || t.Percent > 100 || t.DueDays < 0)) return Bad<List<PaymentTermDto>>("نوع، درصد یا مهلت یکی از مراحل نامعتبر است.");
            if (input.Sum(t => t.Percent) != 100m) return Bad<List<PaymentTermDto>>("مجموع درصدها باید ۱۰۰ باشد.");
            _uow.Set<PurchaseOrderPaymentTerm>().RemoveRange(existing);
            decimal assigned = 0;
            for (int i = 0; i < input.Count; i++)
            {
                decimal amount = i == input.Count - 1 ? order.TotalAmount - assigned : Math.Round(order.TotalAmount * input[i].Percent / 100m, 0);
                assigned += amount;
                _uow.Set<PurchaseOrderPaymentTerm>().Add(new PurchaseOrderPaymentTerm
                {
                    PurchaseOrderId = order.Id, RowNumber = i + 1, Kind = input[i].Kind, Percent = input[i].Percent, Amount = amount, DueDays = input[i].DueDays,
                    Condition = Clean(input[i].Condition), Status = PaymentTermStatus.Pending,
                });
            }
            // the advance allowed on the order is what the terms say (the payables service caps the advance request by it)
            if (input.Any(t => t.Kind == PaymentTermKind.Advance)) order.AdvancePercent = input.Where(t => t.Kind == PaymentTermKind.Advance).Sum(t => t.Percent);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await BuildTermsAsync(order, cancellationToken));
        }

        public async Task<ResponseDto<List<PaymentTermDto>>> ConfirmTermConditionAsync(ProcurementActionDto dto, CancellationToken cancellationToken)
        {
            PurchaseOrderPaymentTerm term = await _uow.Set<PurchaseOrderPaymentTerm>().Include(t => t.PurchaseOrder).ThenInclude(o => o.Lines).FirstOrDefaultAsync(t => t.Id == dto.Id, cancellationToken);
            if (term == null) return ResponseDtoHelper.NotFound<List<PaymentTermDto>>("مرحله پرداخت پیدا نشد.");
            if (term.Kind != PaymentTermKind.RetentionAfterDocuments) return Bad<List<PaymentTermDto>>("فقط مرحله مدارک تایید می‌شود.");
            if (!_user.IsInRole(SupplierContractRoles.DocumentConfirmers)) return Bad<List<PaymentTermDto>>(NotAllowed + " (مهندسی یا کنترل کیفیت)");
            if (term.PurchaseOrder.CreatorUserId == Me || term.PurchaseOrder.ApprovedByUserId == Me) return Bad<List<PaymentTermDto>>(SameUser);
            if (term.Status != PaymentTermStatus.Pending) return Bad<List<PaymentTermDto>>("درخواست پرداخت این مرحله ثبت شده است.");
            term.ConditionConfirmedAt = DateTimeOffset.Now; term.ConditionConfirmedByUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await BuildTermsAsync(term.PurchaseOrder, cancellationToken));
        }

        public async Task<ResponseDto<PaymentRequestDto>> RequestTermPaymentAsync(RequestTermPaymentDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PaymentRequestDto>("اطلاعات ارسال‌شده کامل نیست.");
            if (!_user.IsInRole(ProcurementRoles.Commercial)) return Bad<PaymentRequestDto>(NotAllowed + " (بازرگانی)");
            PurchaseOrderPaymentTerm term = await _uow.Set<PurchaseOrderPaymentTerm>().Include(t => t.PurchaseOrder).ThenInclude(o => o.Lines).FirstOrDefaultAsync(t => t.Id == dto.TermId, cancellationToken);
            if (term == null) return ResponseDtoHelper.NotFound<PaymentRequestDto>("مرحله پرداخت پیدا نشد.");
            List<PaymentTermDto> terms = await BuildTermsAsync(term.PurchaseOrder, cancellationToken);
            PaymentTermDto view = terms.First(t => t.Id == term.Id);
            if (term.Status != PaymentTermStatus.Pending && view.Status != PaymentTermStatus.Pending) return Bad<PaymentRequestDto>("درخواست پرداخت این مرحله ثبت شده است.");
            if (!view.Eligible) return Bad<PaymentRequestDto>("شرط این مرحله برقرار نیست: " + view.EligibleNote);
            var save = new SavePaymentRequestDto
            {
                RequestDate = DateTime.Today, PurchaseOrderId = term.PurchaseOrderId, Amount = term.Amount, Milestone = true,
                Description = $"{TermTitle(term.Kind)} ({term.Percent:0.##} درصد) - سفارش {term.PurchaseOrder.Number}" + (Clean(dto.Note) != null ? " - " + Clean(dto.Note) : ""),
            };
            switch (term.Kind)
            {
                case PaymentTermKind.Advance: save.Type = PaymentRequestType.Advance; break;
                case PaymentTermKind.AfterDeliveryQc: save.Type = PaymentRequestType.Progress; break;
                case PaymentTermKind.ChequeAfterInvoice:
                {
                    save.Type = PaymentRequestType.Invoice;
                    SupplierInvoice invoice = await _uow.Set<SupplierInvoice>().Where(i => i.PurchaseOrderId == term.PurchaseOrderId && i.Status == SupplierInvoiceStatus.Approved && !i.Deleted)
                        .Where(i => !dto.InvoiceId.HasValue || i.Id == dto.InvoiceId.Value).OrderByDescending(i => i.InvoiceDate).FirstOrDefaultAsync(cancellationToken);
                    if (invoice == null) return Bad<PaymentRequestDto>("فاکتور تاییدشده پیدا نشد.");
                    save.InvoiceId = invoice.Id;
                    break;
                }
                default: save.Type = PaymentRequestType.Retention; break;
            }
            ResponseDto<PaymentRequestDto> made = await _payables.SavePaymentAsync(save, cancellationToken);
            if (!made.Success || made.Data == null) return made;
            term.Status = PaymentTermStatus.PaymentRequested;
            term.PaymentRequestId = made.Data.Id;
            await _uow.SaveChangesAsync(cancellationToken);
            return made;
        }

        // ------------------------------------------------------------------------------------------ the printed contract (FO-IP-040-04)

        public async Task<ResponseDto<ContractPrintDto>> GetContractPrintAsync(int purchaseOrderId, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(SupplierContractRoles.Readers)) return Bad<ContractPrintDto>(NotAllowed);
            PurchaseOrder o = await _uow.Set<PurchaseOrder>().AsNoTracking().Include(x => x.Provider).Include(x => x.Lines).ThenInclude(l => l.Item).Include(x => x.Contract)
                .FirstOrDefaultAsync(x => x.Id == purchaseOrderId && !x.Deleted, cancellationToken);
            if (o == null) return ResponseDtoHelper.NotFound<ContractPrintDto>("سفارش پیدا نشد.");
            Dictionary<string, decimal> s = await ProcurementSettingKeys.LoadAsync(_uow, cancellationToken);
            SupplierProfile profile = await _uow.Set<SupplierProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.ProviderId == o.ProviderId, cancellationToken);
            List<PaymentTermDto> terms = await BuildTermsAsync(o, cancellationToken);
            var print = new ContractPrintDto
            {
                TemplateCode = "FO-IP-040-04", TemplateTitle = "قرارداد خرید", OrderNumber = o.Number, ContractNumber = o.ContractNumber ?? o.Number, ContractDate = o.ContractSignedAt ?? o.OrderDate,
                BuyerName = "خریدار", SupplierName = o.Provider?.Name, SupplierNationalId = profile?.NationalId, SupplierEconomicCode = profile?.EconomicCode,
                Subject = Clean(o.Description) ?? "خرید " + string.Join("، ", o.Lines.OrderBy(l => l.RowNumber).Select(l => l.Description ?? l.Item?.Title).Where(t => t != null).Take(3)),
                TotalAmount = o.TotalAmount, TotalInWords = PersianNumber.ToWords(o.TotalAmount) + " ریال", VatPercent = o.VatPercent ?? 0, DeliveryDate = o.DeliveryDate, DeliveryPlace = o.DeliveryPlace,
                ProjectCode = o.Contract?.Code, VolumeLimitPercent = s[ProcurementSettingKeys.AddendumVolumePercent], LatePenaltyPercentPerWeek = o.LatePenaltyPercentPerWeek,
                RetentionPercent = o.RetentionPercent, Requirements = o.Requirements,
            };
            int row = 1;
            foreach (PurchaseOrderLine l in o.Lines.OrderBy(x => x.RowNumber))
                print.Lines.Add(new ContractPrintLineDto { Row = row++, Description = l.Description ?? l.Item?.Title, Unit = l.Unit, Quantity = l.Quantity, UnitPrice = l.UnitPrice, Amount = Math.Round(l.Quantity * l.UnitPrice, 0) });
            foreach (PaymentTermDto t in terms) print.PaymentTerms.Add(new ContractPrintTermDto { Row = t.RowNumber, Title = TermTitle(t.Kind), Percent = t.Percent, Amount = t.Amount, Condition = t.Condition });
            print.Clauses = Clauses(print, s);
            List<SupplierGuarantee> guarantees = await _uow.Set<SupplierGuarantee>().AsNoTracking().Where(g => g.PurchaseOrderId == o.Id && !g.Deleted && g.Status != SupplierGuaranteeStatus.Released).ToListAsync(cancellationToken);
            foreach (SupplierGuarantee g in guarantees) print.Guarantees.Add($"{GuaranteeKindTitle(g.Kind)} شماره {g.GuaranteeNumber} به مبلغ {g.Amount:N0} ریال، صادرکننده {g.Issuer}، اعتبار تا {g.ExpiryDate:yyyy-MM-dd}");
            List<PurchaseOrderAddendum> addenda = await _uow.Set<PurchaseOrderAddendum>().AsNoTracking().Where(a => a.PurchaseOrderId == o.Id && a.Status == AddendumStatus.Applied).OrderBy(a => a.Sequence).ToListAsync(cancellationToken);
            foreach (PurchaseOrderAddendum a in addenda) print.Addenda.Add($"الحاقیه {a.Number}: {a.Reason}");
            return ResponseDtoHelper.Ok(print);
        }

        private static string GuaranteeKindTitle(GuaranteeKind k) => k == GuaranteeKind.Cheque ? "چک" : k == GuaranteeKind.BankGuarantee ? "ضمانت‌نامه بانکی" : "سفته";

        private static List<ContractPrintClauseDto> Clauses(ContractPrintDto p, Dictionary<string, decimal> s)
        {
            string percent = p.VolumeLimitPercent.ToString("0.##");
            decimal penalty = p.LatePenaltyPercentPerWeek ?? 0;
            return new List<ContractPrintClauseDto>
            {
                new ContractPrintClauseDto { Number = "۱", Title = "موضوع", Text = "موضوع قرارداد مطابق جدول اقلام این برگه است. خرید قطعات با نقشه نهایی ممهور به مهر تایید نقشه برای ساخت (AFC) توسط خریدار مورد تایید می‌باشد." },
                new ContractPrintClauseDto { Number = "۲", Title = "ثمن معامله", Text = $"ثمن کل قرارداد {p.TotalAmount:N0} ریال ({p.TotalInWords}) است. خریدار می‌تواند تا {percent} درصد حجم کار قرارداد را کم یا زیاد کند (الحاقیه)." },
                new ContractPrintClauseDto { Number = "۳", Title = "نحوه پرداخت", Text = "پرداخت طبق جدول مراحل پرداخت انجام می‌شود. بهای معامله قطعی است و شامل هیچ‌گونه تعدیلی نمی‌شود." },
                new ContractPrintClauseDto { Number = "۴", Title = "زمان تحویل", Text = p.DeliveryDate.HasValue ? $"تحویل تا تاریخ {p.DeliveryDate:yyyy-MM-dd} در {p.DeliveryPlace ?? "محل تعیین‌شده خریدار"} انجام می‌شود." : "زمان تحویل مطابق توافق ثبت‌شده است." },
                new ContractPrintClauseDto { Number = "۵", Title = "تعهد رازداری", Text = "فروشنده متعهد می‌شود اطلاعات خریدار شامل فرآیندهای ساخت، نقشه‌های دو بعدی و سه بعدی، تصاویر قطعات و اطلاعات ریالی را برای شخص ثالث و در شبکه‌های اجتماعی افشا نکند و از آنها نسخه‌برداری نکند. فروشنده حق حک آرم یا نشان خود را بر روی قطعات ندارد." },
                new ContractPrintClauseDto { Number = "۶", Title = "تعهدات فروشنده", Text = "در صورت دوباره‌کاری ناشی از خطای فروشنده، تهیه متریال و ساخت مجدد با هزینه فروشنده است. اقلام مردود و غیرقابل اصلاح که نتیجه کنترل کیفی آن مورد تایید فروشنده باشد به فروشنده عودت داده نمی‌شود." },
                new ContractPrintClauseDto { Number = "۷", Title = "تضمین انجام تعهدات", Text = "فروشنده چک یا ضمانت‌نامه حسن انجام تعهدات و رازداری را به خریدار تسلیم می‌کند؛ پس از پایان دوره تضمین به فروشنده بازگردانده می‌شود." },
                new ContractPrintClauseDto { Number = "۸", Title = "جریمه", Text = penalty > 0 ? $"به ازای هر هفته تاخیر در تحویل، {penalty:0.##} درصد مبلغ قرارداد به عنوان وجه‌التزام پرداخت می‌شود و پرداخت آن بدل از انجام تعهد نیست. تاخیر بیش از نیمی از مدت قرارداد به خریدار اختیار فسخ می‌دهد." : "جریمه تاخیر طبق توافق طرفین است و پرداخت آن بدل از انجام تعهد نیست. تاخیر بیش از نیمی از مدت قرارداد به خریدار اختیار فسخ می‌دهد." },
                new ContractPrintClauseDto { Number = "۹", Title = "حل اختلاف", Text = "اختلاف ابتدا با مذاکره حل می‌شود و در صورت عدم توافق به مراجع قضایی ارجاع می‌شود. طرفین تا حل نهایی اختلاف به تعهدات خود عمل می‌کنند." },
            };
        }
    }

    /// <summary>Amount in Persian words (up to the trillions), for the printed contract.</summary>
    public static class PersianNumber
    {
        private static readonly string[] Ones = { "", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه", "ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده" };
        private static readonly string[] Tens = { "", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود" };
        private static readonly string[] Hundreds = { "", "یکصد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد" };
        private static readonly string[] Scales = { "", "هزار", "میلیون", "میلیارد", "تریلیون", "کوادریلیون", "کوینتیلیون" };

        public static string ToWords(decimal value)
        {
            long n = (long)Math.Round(Math.Abs(value), 0);
            if (n == 0) return "صفر";
            var parts = new List<string>();
            for (int scale = 0; n > 0; scale++, n /= 1000)
            {
                int group = (int)(n % 1000);
                if (group == 0) continue;
                string text = Group(group);
                parts.Insert(0, Scales[scale].Length > 0 ? text + " " + Scales[scale] : text);
            }
            return (value < 0 ? "منفی " : "") + string.Join(" و ", parts);
        }

        private static string Group(int g)
        {
            var words = new List<string>();
            if (g >= 100) words.Add(Hundreds[g / 100]);
            int rest = g % 100;
            if (rest > 0)
            {
                if (rest < 20) words.Add(Ones[rest]);
                else { words.Add(Tens[rest / 10]); if (rest % 10 > 0) words.Add(Ones[rest % 10]); }
            }
            return string.Join(" و ", words);
        }
    }
}
