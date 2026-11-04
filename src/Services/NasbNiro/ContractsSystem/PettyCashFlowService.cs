using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.FinanceDtos;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.ContractsSystem
{
    public interface IPettyCashFlowService
    {
        Task<ResponseDto<List<PettyRequestDto>>> GetRequestsAsync(FlowQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PettyRequestDto>> SaveRequestAsync(SavePettyRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PettyRequestDto>> RequestActionAsync(FlowActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<List<PettySummaryDto>>> GetSummariesAsync(FlowQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PettySummaryDto>> GetSummaryAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PettySummaryDto>> PreviewSummaryAsync(SavePettySummaryDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PettySummaryDto>> SaveSummaryAsync(SavePettySummaryDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PettySummaryDto>> SummaryActionAsync(FlowActionDto dto, CancellationToken cancellationToken);
    }

    /// <summary>
    /// PB6: the petty-cash flow of the company procedure. A holder asks for a top-up (request, accounting control, finance manager, treasury pays it) and settles the expenses of a period in the
    /// summary FO-AC-005-07 (holder submits, accounting controls, finance manager approves, the managing director gives the final approval). The money itself stays in the existing petty cash,
    /// settlement and payment documents; this service is the flow and the evidence around them.
    /// </summary>
    public class PettyCashFlowService : IPettyCashFlowService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly ITreasuryService _treasury;
        private readonly global::Services.NasbNiro.Office.INotificationService _notify;

        public PettyCashFlowService(IUnitOfWork uow, IInventoryUserContext user, ITreasuryService treasury, global::Services.NasbNiro.Office.INotificationService notify)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _treasury = treasury ?? throw new ArgumentNullException(nameof(treasury));
            _notify = notify;
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private const string NotAllowed = "شما مجوز این کار را ندارید";
        private const string SameUser = "این مرحله باید توسط شخص دیگری غیر از دارنده تنخواه، ثبت‌کننده و تاییدکنندگان قبلی انجام شود.";
        public const string General = "عمومی";

        private bool CanSeeAll => _user.IsInRole(PettyFlowRoles.Readers);

        // =========================================================================================== top-up requests

        private async Task<List<PettyRequestDto>> LoadRequestsAsync(IQueryable<FnPettyCashRequest> source, CancellationToken cancellationToken)
        {
            List<FnPettyCashRequest> rows = await source.Include(r => r.PettyCash).OrderByDescending(r => r.Id).Take(500).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(r => new int?[] { r.CreatorUserId, r.ControlledByUserId, r.ApprovedByUserId, r.PettyCash.HolderUserId }), cancellationToken);
            return rows.Select(r => new PettyRequestDto
            {
                Id = r.Id, Number = r.Number, PettyCashId = r.PettyCashId, HolderName = names.Name(r.PettyCash.HolderUserId), Status = r.Status, Amount = r.Amount, Reason = r.Reason,
                RequestDate = r.RequestDate, Balance = r.PettyCash.Balance, Ceiling = r.PettyCash.Ceiling, SubmittedAt = r.SubmittedAt, ControlledAt = r.ControlledAt,
                ControlledByName = names.Name(r.ControlledByUserId), ApprovedAt = r.ApprovedAt, ApprovedByName = names.Name(r.ApprovedByUserId), ToppedUpAt = r.ToppedUpAt,
                PaymentReference = r.PaymentReference, RejectReason = r.RejectReason, CreatorName = names.Name(r.CreatorUserId),
            }).ToList();
        }

        public async Task<ResponseDto<List<PettyRequestDto>>> GetRequestsAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            query ??= new FlowQuery();
            IQueryable<FnPettyCashRequest> q = _uow.Set<FnPettyCashRequest>().AsNoTracking().Where(r => !r.Deleted);
            if (!CanSeeAll) { int me = Me; q = q.Where(r => r.PettyCash.HolderUserId == me); }
            if (query.PettyCashId.HasValue) q = q.Where(r => r.PettyCashId == query.PettyCashId.Value);
            if (query.Status.HasValue) q = q.Where(r => (int)r.Status == query.Status.Value);
            return ResponseDtoHelper.Ok(await LoadRequestsAsync(q, cancellationToken));
        }

        public async Task<ResponseDto<PettyRequestDto>> SaveRequestAsync(SavePettyRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PettyRequestDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnPettyCash petty = await _uow.Set<FnPettyCash>().FirstOrDefaultAsync(p => p.Id == dto.PettyCashId, cancellationToken);
            if (petty == null || !petty.IsActive) return Bad<PettyRequestDto>("تنخواه پیدا نشد یا غیرفعال است.");
            if (petty.HolderUserId != Me) return Bad<PettyRequestDto>("درخواست شارژ را دارنده تنخواه ثبت می‌کند.");
            if (dto.Amount <= 0) return Bad<PettyRequestDto>("مبلغ باید بیشتر از صفر باشد.");
            if (petty.Balance + dto.Amount > petty.Ceiling) return Bad<PettyRequestDto>($"مبلغ از سقف تنخواه ({petty.Ceiling:N0} ریال) با مانده فعلی ({petty.Balance:N0}) بیشتر می‌شود.");
            if (Clean(dto.Reason) == null) return Bad<PettyRequestDto>("علت درخواست شارژ را بنویسید.");
            FnPettyCashRequest r;
            if (dto.Id.HasValue)
            {
                r = await _uow.Set<FnPettyCashRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (r == null) return ResponseDtoHelper.NotFound<PettyRequestDto>("درخواست پیدا نشد.");
                if (r.CreatorUserId != Me) return Bad<PettyRequestDto>(NotAllowed);
                if (r.Status != FnPettyRequestStatus.Draft) return Bad<PettyRequestDto>("فقط پیش‌نویس ویرایش می‌شود.");
            }
            else
            {
                if (await _uow.Set<FnPettyCashRequest>().AnyAsync(x => x.PettyCashId == petty.Id && !x.Deleted && x.Status != FnPettyRequestStatus.ToppedUp && x.Status != FnPettyRequestStatus.Rejected, cancellationToken))
                    return Bad<PettyRequestDto>("برای این تنخواه درخواست شارژ باز وجود دارد.");
                r = new FnPettyCashRequest
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, Pb6Numbers.PettyRequest, "PCR", DateTime.Today, cancellationToken),
                    PettyCashId = petty.Id, Status = FnPettyRequestStatus.Draft, RequestDate = DateTime.Today, CreateDate = DateTimeOffset.Now, CreatorUserId = Me,
                };
                _uow.Set<FnPettyCashRequest>().Add(r);
            }
            r.PettyCashId = petty.Id; r.Amount = dto.Amount; r.Reason = Clean(dto.Reason);
            r.ModifyDate = DateTimeOffset.Now; r.ModifierUserId = Me;
            if (dto.Submit) { r.Status = FnPettyRequestStatus.Submitted; r.SubmittedAt = DateTimeOffset.Now; }
            await _uow.SaveChangesAsync(cancellationToken);
            await SyncRequestGateAsync(r, cancellationToken);
            return ResponseDtoHelper.Ok((await LoadRequestsAsync(_uow.Set<FnPettyCashRequest>().Where(x => x.Id == r.Id), cancellationToken))[0]);
        }

        private Task SyncRequestGateAsync(FnPettyCashRequest r, CancellationToken cancellationToken)
        {
            string roles = r.Status == FnPettyRequestStatus.Submitted ? PettyFlowRoles.Accounting
                : r.Status == FnPettyRequestStatus.Controlled ? PettyFlowRoles.FinanceManager
                : r.Status == FnPettyRequestStatus.Approved ? PettyFlowRoles.Treasury : null;
            return global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "pcr:" + r.Id, roles != null, "finance", "petty-request", r.Id,
                (r.Status == FnPettyRequestStatus.Approved ? "پرداخت شارژ تنخواه " : "بررسی درخواست شارژ تنخواه ") + r.Number, "/finance/petty-flow?request=" + r.Id, roles, cancellationToken);
        }

        public async Task<ResponseDto<PettyRequestDto>> RequestActionAsync(FlowActionDto dto, CancellationToken cancellationToken)
        {
            FnPettyCashRequest r = await _uow.Set<FnPettyCashRequest>().Include(x => x.PettyCash).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return ResponseDtoHelper.NotFound<PettyRequestDto>("درخواست پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            string note = Clean(dto.Note);
            bool Touched(int user) => user == r.CreatorUserId || user == r.PettyCash.HolderUserId || user == r.ControlledByUserId || user == r.ApprovedByUserId;
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "submit":
                    if (r.Status != FnPettyRequestStatus.Draft) return Bad<PettyRequestDto>("درخواست قبلاً ارسال شده است.");
                    if (r.CreatorUserId != Me) return Bad<PettyRequestDto>(NotAllowed);
                    r.Status = FnPettyRequestStatus.Submitted; r.SubmittedAt = now;
                    break;
                case "control":
                    if (r.Status != FnPettyRequestStatus.Submitted) return Bad<PettyRequestDto>("درخواست در مرحله کنترل حسابداری نیست.");
                    if (!_user.IsInRole(PettyFlowRoles.Accounting)) return Bad<PettyRequestDto>(NotAllowed + " (حسابداری)");
                    if (Touched(Me)) return Bad<PettyRequestDto>(SameUser);
                    r.Status = FnPettyRequestStatus.Controlled; r.ControlledAt = now; r.ControlledByUserId = Me;
                    break;
                case "approve":
                    if (r.Status != FnPettyRequestStatus.Controlled) return Bad<PettyRequestDto>("درخواست باید اول کنترل حسابداری شود.");
                    if (!_user.IsInRole(PettyFlowRoles.FinanceManager)) return Bad<PettyRequestDto>(NotAllowed + " (مدیر مالی)");
                    if (Touched(Me)) return Bad<PettyRequestDto>(SameUser);
                    if (r.PettyCash.Balance + r.Amount > r.PettyCash.Ceiling) return Bad<PettyRequestDto>("با مانده فعلی، مبلغ از سقف تنخواه بیشتر می‌شود.");
                    r.Status = FnPettyRequestStatus.Approved; r.ApprovedAt = now; r.ApprovedByUserId = Me;
                    break;
                case "reject":
                    if (r.Status != FnPettyRequestStatus.Submitted && r.Status != FnPettyRequestStatus.Controlled) return Bad<PettyRequestDto>("درخواست در مرحله بررسی نیست.");
                    if (!_user.IsInRole(PettyFlowRoles.Accounting + "," + PettyFlowRoles.FinanceManager)) return Bad<PettyRequestDto>(NotAllowed);
                    if (Touched(Me)) return Bad<PettyRequestDto>(SameUser);
                    if (note == null) return Bad<PettyRequestDto>("علت رد را بنویسید.");
                    r.Status = FnPettyRequestStatus.Rejected; r.RejectedAt = now; r.RejectedByUserId = Me; r.RejectReason = note;
                    break;
                case "create-payment":
                {
                    // the treasury makes the draft payment (the usual maker-checker of payments applies there); this request then follows it
                    if (r.Status != FnPettyRequestStatus.Approved || r.PaymentReference != null) return Bad<PettyRequestDto>("درخواست تاییدشده‌ای که هنوز سند پرداخت ندارد لازم است.");
                    if (!_user.IsInRole(PettyFlowRoles.Treasury)) return Bad<PettyRequestDto>(NotAllowed + " (خزانه‌داری)");
                    if (!dto.BankAccountId.HasValue) return Bad<PettyRequestDto>("حساب بانکی پرداخت‌کننده را مشخص کنید.");
                    ResponseDto<PaymentDto> payment = await _treasury.SavePaymentAsync(new SavePaymentDto
                    {
                        Date = DateTime.Today, Purpose = FnPaymentPurpose.PettyCashTopUp, PettyCashId = r.PettyCashId, TopUpAmount = r.Amount,
                        Description = "شارژ تنخواه - درخواست " + r.Number,
                        Lines = new List<PaymentLineDto> { new PaymentLineDto { Instrument = FnInstrument.BankTransfer, Amount = r.Amount, BankAccountId = dto.BankAccountId, Reference = Clean(dto.Reference) } },
                    }, cancellationToken);
                    if (payment?.Data == null) return Bad<PettyRequestDto>(payment?.Message ?? "ساخت سند پرداخت ممکن نشد.");
                    r.PaymentReference = payment.Data.Number;
                    break;
                }
                case "confirm-topup":
                {
                    if (r.Status != FnPettyRequestStatus.Approved || r.PaymentReference == null) return Bad<PettyRequestDto>("سند پرداخت این درخواست هنوز ساخته نشده است.");
                    if (!_user.IsInRole(PettyFlowRoles.Treasury + "," + PettyFlowRoles.Accounting)) return Bad<PettyRequestDto>(NotAllowed);
                    FnPayment p = await _uow.Set<FnPayment>().AsNoTracking().FirstOrDefaultAsync(x => x.Number == r.PaymentReference, cancellationToken);
                    if (p == null || p.Status != FnDocStatus.Posted || p.PettyCashId != r.PettyCashId || p.NetAmount != r.Amount)
                        return Bad<PettyRequestDto>("سند پرداخت هنوز ثبت نهایی نشده یا با درخواست نمی‌خواند.");
                    r.Status = FnPettyRequestStatus.ToppedUp; r.ToppedUpAt = now; r.ToppedUpByUserId = Me;
                    break;
                }
                default:
                    return Bad<PettyRequestDto>("عمل نامعتبر است.");
            }
            r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PettyRequestDto>("اطلاعات هم‌زمان تغییر کرد؛ دوباره تلاش کنید."); }
            await SyncRequestGateAsync(r, cancellationToken);
            if (r.Status == FnPettyRequestStatus.Rejected || r.Status == FnPettyRequestStatus.ToppedUp || r.Status == FnPettyRequestStatus.Approved)
                await NotifyAsync(r.PettyCash.HolderUserId, "pcrn:" + r.Id + ":" + r.Status, r.Id, "petty-request",
                    "درخواست شارژ تنخواه " + r.Number + (r.Status == FnPettyRequestStatus.Rejected ? " رد شد: " + r.RejectReason : r.Status == FnPettyRequestStatus.ToppedUp ? " پرداخت شد." : " تایید شد."), cancellationToken);
            return ResponseDtoHelper.Ok((await LoadRequestsAsync(_uow.Set<FnPettyCashRequest>().Where(x => x.Id == r.Id), cancellationToken))[0]);
        }

        private async Task NotifyAsync(int userId, string key, int id, string type, string title, CancellationToken cancellationToken)
        {
            if (_notify == null || userId <= 0 || userId == Me) return;
            try
            {
                await _notify.NotifyAsync(new global::Services.NasbNiro.Office.NotifyRequest
                {
                    SourceKey = key, Module = "finance", SourceType = type, SourceId = id, Title = title, Link = "/finance/petty-flow",
                    Kind = Entities.Model.NasbNiro.Office.OfInboxKind.Alert, UserIds = new List<int> { userId },
                }, cancellationToken);
            }
            catch (Exception)
            {
                // the notice never blocks a step
            }
        }

        // =========================================================================================== the summary FO-AC-005-07

        private async Task<(decimal Previous, decimal Charge)> BalancesAsync(int pettyCashId, DateTime from, DateTime to, CancellationToken cancellationToken)
        {
            IQueryable<FnPayment> topUps = _uow.Set<FnPayment>().AsNoTracking().Where(p => p.PettyCashId == pettyCashId && p.Purpose == FnPaymentPurpose.PettyCashTopUp && p.Status == FnDocStatus.Posted);
            decimal toppedBefore = await topUps.Where(p => p.Date < from).SumAsync(p => (decimal?)p.NetAmount, cancellationToken) ?? 0;
            decimal settledBefore = await _uow.Set<FnPettyCashSettlement>().AsNoTracking().Where(s => s.PettyCashId == pettyCashId && s.Status == FnDocStatus.Posted && s.Date < from)
                .SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0;
            decimal charge = await topUps.Where(p => p.Date >= from && p.Date <= to).SumAsync(p => (decimal?)p.NetAmount, cancellationToken) ?? 0;
            return (toppedBefore - settledBefore, charge);
        }

        private static bool NeedsInvoice(SavePettySummaryLineDto l) =>
            l.ExpenseKind == FnPettyExpenseKind.Goods || l.ExpenseKind == FnPettyExpenseKind.Service || l.PayKind == FnPettyPayKind.Pos || l.PayKind == FnPettyPayKind.Cash;

        /// <summary>The evidence the procedure asks for, per kind of payment and of expense. Returns the missing items (Persian), given the kinds of the files attached to the row.</summary>
        public static List<string> MissingEvidence(SavePettySummaryLineDto l, HashSet<AttachmentKind> kinds, bool hasVehicleForm, bool hasSignedMisc)
        {
            var missing = new List<string>();
            bool Has(AttachmentKind k) => kinds.Contains(k);
            if (l.Amount <= 0) missing.Add("مبلغ");
            if (Clean(l.ProjectCode) == null) missing.Add("کد پروژه یا «عمومی»");
            if (l.ExpenseKind == FnPettyExpenseKind.Miscellaneous)
            {
                // an expense that has no invoice: the signed form FO-AC-003-02 with the project code
                if (!hasSignedMisc) missing.Add("فرم هزینه متفرقه FO-AC-003-02 امضاشده توسط مدیر واحد");
                if (!Has(AttachmentKind.Form)) missing.Add("تصویر فرم هزینه متفرقه امضاشده");
                return missing;
            }
            if (l.ExpenseKind == FnPettyExpenseKind.BankFees)
            {
                if (!Has(AttachmentKind.PaymentSlip)) missing.Add("رسیدهای بانکی کارمزد");
                return missing;
            }
            if (l.ExpenseKind == FnPettyExpenseKind.Goods)
            {
                if (!Has(AttachmentKind.Invoice)) missing.Add("فاکتور");
                if (Clean(l.PrApprovalNumber) == null || l.PrApprovalNumber == "*") missing.Add("شماره تایید درخواست خرید");
                if (Clean(l.WarehouseReceiptNumber) == null || l.WarehouseReceiptNumber == "*") missing.Add("شماره رسید انبار");
                if (l.GoodsClass == FnPettyGoodsClass.QcRequired && !l.ReceiptDocumentId.HasValue) missing.Add("رسید نهایی انبار پس از کنترل کیفی (مواد اولیه، قطعات استاندارد، قطعه نهایی)");
            }
            else
            {
                // services and freight: the PR and receipt columns carry the star
                if (Clean(l.PrApprovalNumber) == null) l.PrApprovalNumber = "*";
                if (Clean(l.WarehouseReceiptNumber) == null) l.WarehouseReceiptNumber = "*";
            }
            if (l.ExpenseKind == FnPettyExpenseKind.Service)
            {
                if (!Has(AttachmentKind.Form)) missing.Add("فرم خدمت");
                if (!Has(AttachmentKind.Invoice)) missing.Add("فاکتور");
                if (!Has(AttachmentKind.PaymentSlip)) missing.Add("فیش پرداخت");
            }
            if (l.ExpenseKind == FnPettyExpenseKind.Freight)
            {
                if (!hasVehicleForm) missing.Add("فرم درخواست خودرو FO-HR-025-02");
                if (l.PayKind == FnPettyPayKind.Waybill && !Has(AttachmentKind.DeliveryNote)) missing.Add("بارنامه (بیجک)");
                if (l.PayKind == FnPettyPayKind.DigitalRide && !Has(AttachmentKind.DeliveryNote) && !Has(AttachmentKind.Invoice)) missing.Add("رسید دیجیتال (اسنپ یا پیک)");
            }
            switch (l.PayKind)
            {
                case FnPettyPayKind.BankTransfer:
                    if (!Has(AttachmentKind.PaymentSlip)) missing.Add("پرینت واریز");
                    if (Clean(l.RecipientName) == null) missing.Add("نام گیرنده در فیش");
                    break;
                case FnPettyPayKind.Pos:
                    if (!Has(AttachmentKind.PaymentSlip)) missing.Add("رسید مشتری دستگاه کارتخوان");
                    if (!Has(AttachmentKind.Invoice)) missing.Add("فاکتور");
                    break;
                case FnPettyPayKind.Online:
                    if (!Has(AttachmentKind.PaymentSlip)) missing.Add("رسید پرداخت اینترنتی");
                    if (Clean(l.TrackingNumber) == null) missing.Add("شماره پیگیری");
                    break;
                case FnPettyPayKind.Cash:
                    if (l.ExpenseKind != FnPettyExpenseKind.Freight && !Has(AttachmentKind.Invoice)) missing.Add("فاکتور");
                    break;
            }
            return missing.Distinct().ToList();
        }

        private async Task<Dictionary<int, HashSet<AttachmentKind>>> KindsAsync(List<int> lineIds, CancellationToken cancellationToken)
        {
            var rows = await _uow.Set<RecordAttachment>().AsNoTracking().Where(a => a.OwnerType == AttachmentOwnerType.PettyCashSummaryLine && lineIds.Contains(a.OwnerId) && !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null)
                .Select(a => new { a.OwnerId, a.Kind }).ToListAsync(cancellationToken);
            return rows.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.Select(a => a.Kind).ToHashSet());
        }

        private async Task<List<PettySummaryDto>> LoadSummariesAsync(IQueryable<FnPettyCashSummary> source, CancellationToken cancellationToken)
        {
            List<FnPettyCashSummary> rows = await source.Include(s => s.PettyCash).Include(s => s.Lines).AsSplitQuery().OrderByDescending(s => s.Id).Take(300).ToListAsync(cancellationToken);
            List<int> lineIds = rows.SelectMany(s => s.Lines.Select(l => l.Id)).ToList();
            Dictionary<int, HashSet<AttachmentKind>> kinds = await KindsAsync(lineIds, cancellationToken);
            var counts = await _uow.Set<RecordAttachment>().AsNoTracking().Where(a => a.OwnerType == AttachmentOwnerType.PettyCashSummaryLine && lineIds.Contains(a.OwnerId) && !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null)
                .GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            var vehicle = await _uow.Set<FnPettyCashVehicleForm>().AsNoTracking().Where(v => lineIds.Contains(v.LineId)).ToListAsync(cancellationToken);
            var misc = await _uow.Set<FnPettyCashMiscForm>().AsNoTracking().Where(v => lineIds.Contains(v.LineId)).ToListAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.SelectMany(s => new int?[] { s.CreatorUserId, s.ControlledByUserId, s.ApprovedByUserId, s.FinalByUserId, s.PettyCash.HolderUserId }), cancellationToken);
            var result = new List<PettySummaryDto>();
            foreach (FnPettyCashSummary s in rows)
            {
                var dto = new PettySummaryDto
                {
                    Id = s.Id, Number = s.Number, PettyCashId = s.PettyCashId, HolderName = names.Name(s.PettyCash.HolderUserId), Date = s.Date, PeriodFrom = s.PeriodFrom, PeriodTo = s.PeriodTo, Status = s.Status,
                    AttachmentsCount = s.AttachmentsCount, PreviousBalance = s.PreviousBalance, Charge = s.Charge, TotalAmount = s.TotalAmount, Balance = s.Balance, TotalInWords = s.TotalInWords,
                    Notes = s.Notes, ReturnReason = s.ReturnReason, SubmittedAt = s.SubmittedAt, ControlledAt = s.ControlledAt, ControlledByName = names.Name(s.ControlledByUserId),
                    ApprovedAt = s.ApprovedAt, ApprovedByName = names.Name(s.ApprovedByUserId), FinalAt = s.FinalAt, FinalByName = names.Name(s.FinalByUserId), SettlementId = s.SettlementId,
                    CreatorName = names.Name(s.CreatorUserId),
                };
                foreach (FnPettyCashSummaryLine l in s.Lines.OrderBy(x => x.RowNumber))
                {
                    var line = new PettySummaryLineDto
                    {
                        Id = l.Id, RowNumber = l.RowNumber, Date = l.Date, InvoiceNumber = l.InvoiceNumber, PrApprovalNumber = l.PrApprovalNumber, WarehouseReceiptNumber = l.WarehouseReceiptNumber,
                        ProjectCode = l.ProjectCode, Description = l.Description, Amount = l.Amount, Channel = l.Channel, DocType = l.DocType, ExpenseKind = l.ExpenseKind, GoodsClass = l.GoodsClass,
                        PayKind = l.PayKind, RecipientName = l.RecipientName, TrackingNumber = l.TrackingNumber, SettlementLineId = l.SettlementLineId, PurchaseRequestId = l.PurchaseRequestId,
                        ReceiptDocumentId = l.ReceiptDocumentId, AttachmentCount = counts.FirstOrDefault(c => c.Key == l.Id)?.Count ?? 0,
                    };
                    FnPettyCashVehicleForm v = vehicle.FirstOrDefault(x => x.LineId == l.Id);
                    if (v != null) line.Vehicle = new PettyVehicleFormDto { Date = v.Date, Unit = v.Unit, VehicleType = v.VehicleType, Origin = v.Origin, Destination = v.Destination, Description = v.Description };
                    FnPettyCashMiscForm m = misc.FirstOrDefault(x => x.LineId == l.Id);
                    if (m != null) line.Misc = new PettyMiscFormDto { RequestingUnit = m.RequestingUnit, ProjectCode = m.ProjectCode, WhyNoInvoice = m.WhyNoInvoice, Description = m.Description, Signed = m.SignedAt.HasValue };
                    line.Missing = MissingEvidence(line, kinds.TryGetValue(l.Id, out HashSet<AttachmentKind> k) ? k : new HashSet<AttachmentKind>(), v != null, m?.SignedAt != null);
                    if (line.Missing.Count > 0) dto.Problems.Add($"ردیف {l.RowNumber}: " + string.Join("، ", line.Missing));
                    dto.Lines.Add(line);
                }
                if (dto.Lines.Count(x => x.ExpenseKind == FnPettyExpenseKind.BankFees) > 1) dto.Problems.Add("کارمزدهای بانکی دوره باید در یک ردیف جمع شود.");
                if (dto.Lines.Count == 0) dto.Problems.Add("صورت‌خلاصه هیچ ردیفی ندارد.");
                result.Add(dto);
            }
            return result;
        }

        public async Task<ResponseDto<List<PettySummaryDto>>> GetSummariesAsync(FlowQuery query, CancellationToken cancellationToken)
        {
            query ??= new FlowQuery();
            IQueryable<FnPettyCashSummary> q = _uow.Set<FnPettyCashSummary>().AsNoTracking().Where(s => !s.Deleted);
            if (!CanSeeAll) { int me = Me; q = q.Where(s => s.PettyCash.HolderUserId == me); }
            if (query.PettyCashId.HasValue) q = q.Where(s => s.PettyCashId == query.PettyCashId.Value);
            if (query.Status.HasValue) q = q.Where(s => (int)s.Status == query.Status.Value);
            return ResponseDtoHelper.Ok(await LoadSummariesAsync(q, cancellationToken));
        }

        public async Task<ResponseDto<PettySummaryDto>> GetSummaryAsync(int id, CancellationToken cancellationToken)
        {
            List<PettySummaryDto> list = await LoadSummariesAsync(_uow.Set<FnPettyCashSummary>().AsNoTracking().Where(s => s.Id == id && !s.Deleted), cancellationToken);
            if (list.Count == 0) return ResponseDtoHelper.NotFound<PettySummaryDto>("صورت‌خلاصه پیدا نشد.");
            FnPettyCash petty = await _uow.Set<FnPettyCash>().AsNoTracking().FirstAsync(p => p.Id == list[0].PettyCashId, cancellationToken);
            if (!CanSeeAll && petty.HolderUserId != Me) return ResponseDtoHelper.NotFound<PettySummaryDto>("صورت‌خلاصه پیدا نشد.");
            return ResponseDtoHelper.Ok(list[0]);
        }

        /// <summary>The summary of a period filled from the settlement lines of the holder (not yet in another summary), with the header numbers computed.</summary>
        private async Task<List<SavePettySummaryLineDto>> AutoLinesAsync(FnPettyCash petty, DateTime from, DateTime to, int exceptSummaryId, CancellationToken cancellationToken)
        {
            List<int> used = await _uow.Set<FnPettyCashSummaryLine>().AsNoTracking()
                .Where(l => l.SettlementLineId != null && l.Summary.Id != exceptSummaryId && l.Summary.Status != FnSummaryStatus.Rejected && !l.Summary.Deleted).Select(l => l.SettlementLineId.Value).ToListAsync(cancellationToken);
            var rows = await _uow.Set<FnPettyCashSettlementLine>().AsNoTracking()
                .Where(l => l.Settlement.PettyCashId == petty.Id && l.Settlement.Status != FnDocStatus.Cancelled && l.Settlement.Date >= from && l.Settlement.Date <= to && !used.Contains(l.Id))
                .OrderBy(l => l.Settlement.Date).ThenBy(l => l.Id)
                .Select(l => new
                {
                    l.Id, l.Settlement.Date, l.Description, l.SupplierName, l.Amount, l.VatAmount, l.InventoryDocumentId, l.WorkOrderId,
                    ProjectCode = l.WorkOrderId == null ? null : _uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().Where(w => w.Id == l.WorkOrderId).Select(w => w.Contract.Code).FirstOrDefault(),
                    Receipt = l.InventoryDocumentId == null ? null : _uow.Set<InventoryDocument>().Where(d => d.Id == l.InventoryDocumentId).Select(d => d.Number).FirstOrDefault(),
                }).ToListAsync(cancellationToken);
            return rows.Select(r => new SavePettySummaryLineDto
            {
                Date = r.Date, Description = Clean(r.Description) ?? r.SupplierName, Amount = r.Amount + r.VatAmount, SettlementLineId = r.Id, ProjectCode = r.ProjectCode ?? General,
                ExpenseKind = r.InventoryDocumentId.HasValue ? FnPettyExpenseKind.Goods : FnPettyExpenseKind.Service, ReceiptDocumentId = r.InventoryDocumentId,
                WarehouseReceiptNumber = r.Receipt ?? "*", PrApprovalNumber = r.InventoryDocumentId.HasValue ? null : "*",
                Channel = FnPettyChannel.PettyCash, DocType = FnPettyDocType.Invoice, PayKind = FnPettyPayKind.Cash,
            }).ToList();
        }

        public async Task<ResponseDto<PettySummaryDto>> PreviewSummaryAsync(SavePettySummaryDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PettySummaryDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnPettyCash petty = await _uow.Set<FnPettyCash>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PettyCashId, cancellationToken);
            if (petty == null) return ResponseDtoHelper.NotFound<PettySummaryDto>("تنخواه پیدا نشد.");
            if (petty.HolderUserId != Me && !CanSeeAll) return Bad<PettySummaryDto>(NotAllowed);
            if (dto.PeriodTo.Date < dto.PeriodFrom.Date) return Bad<PettySummaryDto>("پایان دوره از آغاز آن زودتر است.");
            var lines = await AutoLinesAsync(petty, dto.PeriodFrom.Date, dto.PeriodTo.Date, dto.Id ?? 0, cancellationToken);
            var balances = await BalancesAsync(petty.Id, dto.PeriodFrom.Date, dto.PeriodTo.Date, cancellationToken);
            decimal total = lines.Sum(l => l.Amount);
            var view = new PettySummaryDto
            {
                PettyCashId = petty.Id, Date = DateTime.Today, PeriodFrom = dto.PeriodFrom.Date, PeriodTo = dto.PeriodTo.Date, PreviousBalance = balances.Previous, Charge = balances.Charge,
                TotalAmount = total, Balance = balances.Previous + balances.Charge - total, TotalInWords = PersianNumber.ToWords(total) + " ریال",
                Lines = lines.Select((l, i) => new PettySummaryLineDto
                {
                    RowNumber = i + 1, Date = l.Date, Description = l.Description, Amount = l.Amount, SettlementLineId = l.SettlementLineId, ProjectCode = l.ProjectCode, ExpenseKind = l.ExpenseKind,
                    ReceiptDocumentId = l.ReceiptDocumentId, WarehouseReceiptNumber = l.WarehouseReceiptNumber, PrApprovalNumber = l.PrApprovalNumber, Channel = l.Channel, DocType = l.DocType, PayKind = l.PayKind,
                }).ToList(),
            };
            return ResponseDtoHelper.Ok(view);
        }

        public async Task<ResponseDto<PettySummaryDto>> SaveSummaryAsync(SavePettySummaryDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PettySummaryDto>("اطلاعات ارسال‌شده کامل نیست.");
            FnPettyCash petty = await _uow.Set<FnPettyCash>().FirstOrDefaultAsync(p => p.Id == dto.PettyCashId, cancellationToken);
            if (petty == null || !petty.IsActive) return Bad<PettySummaryDto>("تنخواه پیدا نشد یا غیرفعال است.");
            if (petty.HolderUserId != Me) return Bad<PettySummaryDto>("صورت‌خلاصه را دارنده تنخواه ثبت می‌کند.");
            DateTime from = dto.PeriodFrom.Date, to = dto.PeriodTo.Date;
            if (to < from) return Bad<PettySummaryDto>("پایان دوره از آغاز آن زودتر است.");
            FnPettyCashSummary s;
            if (dto.Id.HasValue)
            {
                s = await _uow.Set<FnPettyCashSummary>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (s == null) return ResponseDtoHelper.NotFound<PettySummaryDto>("صورت‌خلاصه پیدا نشد.");
                if (s.CreatorUserId != Me) return Bad<PettySummaryDto>(NotAllowed);
                if (s.Status != FnSummaryStatus.Draft && s.Status != FnSummaryStatus.Returned) return Bad<PettySummaryDto>("فقط پیش‌نویس یا برگشتی ویرایش می‌شود.");
            }
            else
            {
                s = new FnPettyCashSummary
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, Pb6Numbers.PettySummary, "PCSUM", DateTime.Today, cancellationToken), PettyCashId = petty.Id, Status = FnSummaryStatus.Draft,
                    Date = DateTime.Today, CreateDate = DateTimeOffset.Now, CreatorUserId = Me, Lines = new List<FnPettyCashSummaryLine>(),
                };
                _uow.Set<FnPettyCashSummary>().Add(s);
            }
            List<SavePettySummaryLineDto> input = dto.AutoFill ? await AutoLinesAsync(petty, from, to, s.Id, cancellationToken) : (dto.Lines ?? new List<SavePettySummaryLineDto>()).Where(l => l != null).ToList();
            for (int i = 0; i < input.Count; i++)
            {
                SavePettySummaryLineDto l = input[i];
                if (!Enum.IsDefined(typeof(FnPettyExpenseKind), l.ExpenseKind) || !Enum.IsDefined(typeof(FnPettyPayKind), l.PayKind) || !Enum.IsDefined(typeof(FnPettyChannel), l.Channel) || !Enum.IsDefined(typeof(FnPettyDocType), l.DocType))
                    return Bad<PettySummaryDto>($"ردیف {i + 1}: نوع هزینه، پرداخت، کانال یا سند نامعتبر است.");
                if (l.Amount < 0) return Bad<PettySummaryDto>($"ردیف {i + 1}: مبلغ نمی‌تواند منفی باشد.");
                if (l.Date.HasValue && (l.Date.Value.Date < from || l.Date.Value.Date > to)) return Bad<PettySummaryDto>($"ردیف {i + 1}: تاریخ بیرون از دوره است.");
            }
            List<int> wantedSettlementLines = input.Where(l => l.SettlementLineId.HasValue).Select(l => l.SettlementLineId.Value).ToList();
            if (wantedSettlementLines.Distinct().Count() != wantedSettlementLines.Count) return Bad<PettySummaryDto>("یک ردیف تسویه بیش از یک بار آمده است.");
            if (wantedSettlementLines.Count > 0 && await _uow.Set<FnPettyCashSummaryLine>().AnyAsync(l => l.SettlementLineId != null && wantedSettlementLines.Contains(l.SettlementLineId.Value)
                    && l.SummaryId != s.Id && l.Summary.Status != FnSummaryStatus.Rejected && !l.Summary.Deleted, cancellationToken))
                return Bad<PettySummaryDto>("یکی از ردیف‌های تسویه در صورت‌خلاصه دیگری آمده است.");
            // existing rows keep their id (their attachments stay); the rest is replaced
            List<int> keep = input.Where(l => l.Id.HasValue).Select(l => l.Id.Value).ToList();
            List<FnPettyCashSummaryLine> dropped = s.Lines.Where(l => !keep.Contains(l.Id)).ToList();
            if (dropped.Count > 0)
            {
                List<int> droppedIds = dropped.Select(l => l.Id).ToList();
                if (await _uow.Set<RecordAttachment>().AnyAsync(a => a.OwnerType == AttachmentOwnerType.PettyCashSummaryLine && droppedIds.Contains(a.OwnerId) && !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null, cancellationToken))
                    return Bad<PettySummaryDto>("ردیفی که مدرک پیوست دارد حذف نمی‌شود؛ ابتدا مدارک آن را ابطال کنید.");
                _uow.Set<FnPettyCashVehicleForm>().RemoveRange(await _uow.Set<FnPettyCashVehicleForm>().Where(v => droppedIds.Contains(v.LineId)).ToListAsync(cancellationToken));
                _uow.Set<FnPettyCashMiscForm>().RemoveRange(await _uow.Set<FnPettyCashMiscForm>().Where(v => droppedIds.Contains(v.LineId)).ToListAsync(cancellationToken));
                _uow.Set<FnPettyCashSummaryLine>().RemoveRange(dropped);
                foreach (FnPettyCashSummaryLine d in dropped) s.Lines.Remove(d);
            }
            int row = 1;
            foreach (SavePettySummaryLineDto l in input)
            {
                FnPettyCashSummaryLine line = l.Id.HasValue ? s.Lines.FirstOrDefault(x => x.Id == l.Id.Value) : null;
                if (line == null) { line = new FnPettyCashSummaryLine(); s.Lines.Add(line); }
                line.RowNumber = row++; line.Date = l.Date?.Date; line.InvoiceNumber = Clean(l.InvoiceNumber); line.PrApprovalNumber = Clean(l.PrApprovalNumber); line.WarehouseReceiptNumber = Clean(l.WarehouseReceiptNumber);
                line.ProjectCode = Clean(l.ProjectCode) ?? General; line.Description = Clean(l.Description); line.Amount = l.Amount; line.Channel = l.Channel; line.DocType = l.DocType;
                line.ExpenseKind = l.ExpenseKind; line.GoodsClass = l.GoodsClass; line.PayKind = l.PayKind; line.RecipientName = Clean(l.RecipientName); line.TrackingNumber = Clean(l.TrackingNumber);
                line.SettlementLineId = l.SettlementLineId; line.PurchaseRequestId = l.PurchaseRequestId; line.ReceiptDocumentId = l.ReceiptDocumentId;
                if (line.ExpenseKind != FnPettyExpenseKind.Goods)
                {
                    line.PrApprovalNumber ??= "*"; line.WarehouseReceiptNumber ??= "*";   // services: the star of the form
                }
            }
            await _uow.SaveChangesAsync(cancellationToken);   // the lines need their ids for the forms
            // the vehicle form (freight) and the miscellaneous expense form hang on the saved rows
            for (int i = 0; i < input.Count; i++)
            {
                FnPettyCashSummaryLine line = s.Lines.OrderBy(x => x.RowNumber).ElementAt(i);
                SavePettySummaryLineDto l = input[i];
                FnPettyCashVehicleForm v = await _uow.Set<FnPettyCashVehicleForm>().FirstOrDefaultAsync(x => x.LineId == line.Id, cancellationToken);
                if (l.ExpenseKind == FnPettyExpenseKind.Freight && l.Vehicle != null && Clean(l.Vehicle.Origin) != null && Clean(l.Vehicle.Destination) != null && Clean(l.Vehicle.VehicleType) != null && Clean(l.Vehicle.Unit) != null)
                {
                    if (v == null) { v = new FnPettyCashVehicleForm { LineId = line.Id, RequestedByUserId = Me }; _uow.Set<FnPettyCashVehicleForm>().Add(v); }
                    v.Date = l.Vehicle.Date == default ? (line.Date ?? DateTime.Today) : l.Vehicle.Date.Date; v.Unit = Clean(l.Vehicle.Unit); v.VehicleType = Clean(l.Vehicle.VehicleType);
                    v.Origin = Clean(l.Vehicle.Origin); v.Destination = Clean(l.Vehicle.Destination); v.Description = Clean(l.Vehicle.Description);
                }
                else if (v != null) _uow.Set<FnPettyCashVehicleForm>().Remove(v);
                FnPettyCashMiscForm m = await _uow.Set<FnPettyCashMiscForm>().FirstOrDefaultAsync(x => x.LineId == line.Id, cancellationToken);
                if (l.ExpenseKind == FnPettyExpenseKind.Miscellaneous && l.Misc != null)
                {
                    if (m == null) { m = new FnPettyCashMiscForm { LineId = line.Id, RequestedByUserId = Me }; _uow.Set<FnPettyCashMiscForm>().Add(m); }
                    string newUnit = Clean(l.Misc.RequestingUnit), newProject = Clean(l.Misc.ProjectCode) ?? line.ProjectCode, newWhy = Clean(l.Misc.WhyNoInvoice), newDesc = Clean(l.Misc.Description) ?? line.Description;
                    // M8: a signature belongs to the content it was given on: changed content clears it
                    if (m.SignedAt.HasValue && (m.RequestingUnit != newUnit || m.ProjectCode != newProject || m.WhyNoInvoice != newWhy || m.Description != newDesc || m.Amount != line.Amount))
                    { m.SignedAt = null; m.UnitManagerUserId = null; }
                    m.RequestingUnit = newUnit; m.ProjectCode = newProject; m.WhyNoInvoice = newWhy;
                    m.Description = newDesc; m.Amount = line.Amount;
                    // the unit manager signs, not the holder: the signature is recorded by the "sign-misc" action of that manager
                }
                else if (m != null) _uow.Set<FnPettyCashMiscForm>().Remove(m);
            }
            var balances = await BalancesAsync(petty.Id, from, to, cancellationToken);
            s.PettyCashId = petty.Id; s.PeriodFrom = from; s.PeriodTo = to; s.Notes = Clean(dto.Notes);
            s.PreviousBalance = balances.Previous; s.Charge = balances.Charge;
            s.TotalAmount = s.Lines.Sum(l => l.Amount); s.Balance = s.PreviousBalance + s.Charge - s.TotalAmount; s.TotalInWords = PersianNumber.ToWords(s.TotalAmount) + " ریال";
            List<int> savedLineIds = s.Lines.Select(l => l.Id).ToList();
            s.AttachmentsCount = await _uow.Set<RecordAttachment>().CountAsync(a => a.OwnerType == AttachmentOwnerType.PettyCashSummaryLine && savedLineIds.Contains(a.OwnerId) && !a.Deleted && a.RemovedAt == null && !a.Voided && a.SupersededById == null, cancellationToken);
            s.Status = FnSummaryStatus.Draft; s.ReturnReason = null;
            s.EditorUserIds = LedgerMath.WithEditor(s.EditorUserIds, Me);
            s.ModifyDate = DateTimeOffset.Now; s.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return await GetSummaryAsync(s.Id, cancellationToken);
        }

        private Task SyncSummaryGateAsync(FnPettyCashSummary s, CancellationToken cancellationToken)
        {
            string roles = s.Status == FnSummaryStatus.Submitted ? PettyFlowRoles.Accounting
                : s.Status == FnSummaryStatus.Controlled ? PettyFlowRoles.FinanceManager
                : s.Status == FnSummaryStatus.Approved ? PettyFlowRoles.Ceo : null;
            return global::Services.NasbNiro.Office.OfficeGates.SyncAsync(_notify, "pcs:" + s.Id, roles != null, "finance", "petty-summary", s.Id,
                "صورت‌خلاصه تنخواه " + s.Number, "/finance/petty-flow?summary=" + s.Id, roles, cancellationToken);
        }

        public async Task<ResponseDto<PettySummaryDto>> SummaryActionAsync(FlowActionDto dto, CancellationToken cancellationToken)
        {
            FnPettyCashSummary s = await _uow.Set<FnPettyCashSummary>().Include(x => x.PettyCash).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (s == null) return ResponseDtoHelper.NotFound<PettySummaryDto>("صورت‌خلاصه پیدا نشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            string note = Clean(dto.Note);
            // M8: whoever signed a miscellaneous form of this summary is part of its making and never approves it
            List<int> formLineIds = s.Lines.Select(l => l.Id).ToList();
            List<int> signers = await _uow.Set<FnPettyCashMiscForm>().Where(m => formLineIds.Contains(m.LineId) && m.UnitManagerUserId != null).Select(m => m.UnitManagerUserId.Value).ToListAsync(cancellationToken);
            bool Touched(int user) => user == s.CreatorUserId || user == s.PettyCash.HolderUserId || user == s.ControlledByUserId || user == s.ApprovedByUserId || (s.EditorUserIds != null && s.EditorUserIds.Contains("," + user + ",")) || signers.Contains(user);
            switch (dto.Action?.Trim().ToLowerInvariant())
            {
                case "sign-misc":
                {
                    // the miscellaneous expense form is signed by the manager of the requesting unit: not the holder and not the maker of the summary
                    if (s.Status != FnSummaryStatus.Draft && s.Status != FnSummaryStatus.Returned) return Bad<PettySummaryDto>("فرم را فقط پیش از ارسال صورت‌خلاصه امضا می‌کنند.");
                    if (Touched(Me) && !signers.Contains(Me) || s.CreatorUserId == Me || s.PettyCash.HolderUserId == Me) return Bad<PettySummaryDto>("فرم هزینه متفرقه را مدیر واحد درخواست‌کننده امضا می‌کند، نه دارنده تنخواه.");
                    if (!_user.IsInRole("PMO,PLANNING,PROJECTMANAGER,PRODUCTIONMANAGER,SHOPSUPERVISOR,SALES,SERVICE,ENGINEERING,QC,WAREHOUSE,WHSUPERVISOR,COMMERCIAL,HR,SECRETARIAT,FINANCEMANAGER")) return Bad<PettySummaryDto>(NotAllowed + " (مدیر واحد)");
                    List<int> lineIds = s.Lines.Select(l => l.Id).ToList();
                    FnPettyCashMiscForm form = await _uow.Set<FnPettyCashMiscForm>().FirstOrDefaultAsync(m => lineIds.Contains(m.LineId) && m.LineId == (dto.LineId ?? 0), cancellationToken);
                    if (form == null) return Bad<PettySummaryDto>("برای امضا، شناسه ردیف دارای فرم هزینه متفرقه را در LineId بفرستید.");
                    form.UnitManagerUserId = Me; form.SignedAt = now;
                    break;
                }
                case "submit":
                {
                    if (s.Status != FnSummaryStatus.Draft && s.Status != FnSummaryStatus.Returned) return Bad<PettySummaryDto>("صورت‌خلاصه قبلاً ارسال شده است.");
                    if (s.PettyCash.HolderUserId != Me) return Bad<PettySummaryDto>("فقط دارنده تنخواه صورت‌خلاصه را امضا و ارسال می‌کند.");
                    PettySummaryDto view = (await LoadSummariesAsync(_uow.Set<FnPettyCashSummary>().AsNoTracking().Where(x => x.Id == s.Id), cancellationToken))[0];
                    if (view.Problems.Count > 0) return Bad<PettySummaryDto>("مدارک کامل نیست: " + string.Join(" | ", view.Problems.Take(5)));
                    s.Status = FnSummaryStatus.Submitted; s.SubmittedAt = now; s.ReturnReason = null;
                    break;
                }
                case "control":
                    if (s.Status != FnSummaryStatus.Submitted) return Bad<PettySummaryDto>("صورت‌خلاصه در مرحله کنترل حسابداری نیست.");
                    if (!_user.IsInRole(PettyFlowRoles.Accounting)) return Bad<PettySummaryDto>(NotAllowed + " (حسابداری)");
                    if (Touched(Me)) return Bad<PettySummaryDto>(SameUser);
                    s.Status = FnSummaryStatus.Controlled; s.ControlledAt = now; s.ControlledByUserId = Me;
                    break;
                case "approve":
                    if (s.Status != FnSummaryStatus.Controlled) return Bad<PettySummaryDto>("صورت‌خلاصه باید اول کنترل حسابداری شود.");
                    if (!_user.IsInRole(PettyFlowRoles.FinanceManager)) return Bad<PettySummaryDto>(NotAllowed + " (مدیر مالی)");
                    if (Touched(Me)) return Bad<PettySummaryDto>(SameUser);
                    s.Status = FnSummaryStatus.Approved; s.ApprovedAt = now; s.ApprovedByUserId = Me;
                    break;
                case "final":
                    if (s.Status != FnSummaryStatus.Approved) return Bad<PettySummaryDto>("صورت‌خلاصه باید اول توسط مدیر مالی تایید شود.");
                    if (!_user.IsInRole(PettyFlowRoles.Ceo)) return Bad<PettySummaryDto>(NotAllowed + " (مدیرعامل)");
                    if (Touched(Me)) return Bad<PettySummaryDto>(SameUser);
                    {
                        // M8: the summary ties to the books: every row is backed by a settlement line of a posted settlement of this petty cash, and the totals are equal
                        if (s.Lines.Any(l => !l.SettlementLineId.HasValue)) return Bad<PettySummaryDto>("هر ردیف صورت‌خلاصه باید به ردیف تسویه ثبت‌شده در دفتر وصل باشد؛ تسویه را ثبت و ردیف را وصل کنید.");
                        List<int> settlementLineIds = s.Lines.Select(l => l.SettlementLineId.Value).ToList();
                        if (settlementLineIds.Distinct().Count() != settlementLineIds.Count) return Bad<PettySummaryDto>("یک ردیف تسویه بیش از یک بار در صورت‌خلاصه آمده است.");
                        var booked = await _uow.Set<FnPettyCashSettlementLine>().AsNoTracking().Where(l => settlementLineIds.Contains(l.Id))
                            .Select(l => new { l.Id, l.Amount, l.VatAmount, l.Settlement.Status, l.Settlement.PettyCashId }).ToListAsync(cancellationToken);
                        if (booked.Count != settlementLineIds.Count || booked.Any(b => b.Status != FnDocStatus.Posted || b.PettyCashId != s.PettyCashId))
                            return Bad<PettySummaryDto>("ردیف‌های صورت‌خلاصه باید به تسویه‌های قطعی‌شده (ثبت‌شده در دفتر) همین تنخواه وصل باشند.");
                        decimal bookedTotal = booked.Sum(b => b.Amount + b.VatAmount);
                        if (Math.Round(bookedTotal, 0) != Math.Round(s.Lines.Sum(l => l.Amount), 0)) return Bad<PettySummaryDto>($"جمع صورت‌خلاصه ({s.Lines.Sum(l => l.Amount):N0}) با جمع تسویه‌های ثبت‌شده ({bookedTotal:N0}) برابر نیست.");
                        if (s.TotalAmount != s.Lines.Sum(l => l.Amount)) return Bad<PettySummaryDto>("جمع کل صورت‌خلاصه با ردیف‌ها نمی‌خواند؛ دوباره ذخیره کنید.");
                    }
                    s.Status = FnSummaryStatus.Final; s.FinalAt = now; s.FinalByUserId = Me;
                    List<int> settlements = await _uow.Set<FnPettyCashSettlementLine>().Where(l => s.Lines.Where(x => x.SettlementLineId != null).Select(x => x.SettlementLineId.Value).Contains(l.Id)).Select(l => l.SettlementId).Distinct().ToListAsync(cancellationToken);
                    if (settlements.Count == 1) s.SettlementId = settlements[0];
                    break;
                case "return":
                    if (s.Status != FnSummaryStatus.Submitted && s.Status != FnSummaryStatus.Controlled && s.Status != FnSummaryStatus.Approved) return Bad<PettySummaryDto>("صورت‌خلاصه در مرحله بررسی نیست.");
                    if (!_user.IsInRole(PettyFlowRoles.Accounting + "," + PettyFlowRoles.FinanceManager + "," + PettyFlowRoles.Ceo)) return Bad<PettySummaryDto>(NotAllowed);
                    if (Touched(Me)) return Bad<PettySummaryDto>(SameUser);
                    if (note == null) return Bad<PettySummaryDto>("علت برگشت را بنویسید.");
                    s.Status = FnSummaryStatus.Returned; s.ReturnReason = note; s.ControlledAt = null; s.ControlledByUserId = null; s.ApprovedAt = null; s.ApprovedByUserId = null;
                    break;
                case "reject":
                    if (s.Status != FnSummaryStatus.Submitted && s.Status != FnSummaryStatus.Controlled && s.Status != FnSummaryStatus.Approved) return Bad<PettySummaryDto>("صورت‌خلاصه در مرحله بررسی نیست.");
                    if (!_user.IsInRole(PettyFlowRoles.FinanceManager + "," + PettyFlowRoles.Ceo)) return Bad<PettySummaryDto>(NotAllowed);
                    if (Touched(Me)) return Bad<PettySummaryDto>(SameUser);
                    if (note == null) return Bad<PettySummaryDto>("علت رد را بنویسید.");
                    s.Status = FnSummaryStatus.Rejected; s.ReturnReason = note;
                    break;
                default:
                    return Bad<PettySummaryDto>("عمل نامعتبر است.");
            }
            s.ModifyDate = now; s.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PettySummaryDto>("اطلاعات هم‌زمان تغییر کرد؛ دوباره تلاش کنید."); }
            await SyncSummaryGateAsync(s, cancellationToken);
            if (s.Status == FnSummaryStatus.Returned || s.Status == FnSummaryStatus.Rejected || s.Status == FnSummaryStatus.Final)
                await NotifyAsync(s.PettyCash.HolderUserId, "pcsn:" + s.Id + ":" + s.Status, s.Id, "petty-summary",
                    "صورت‌خلاصه " + s.Number + (s.Status == FnSummaryStatus.Final ? " تایید نهایی شد." : s.Status == FnSummaryStatus.Returned ? " برگشت خورد: " + s.ReturnReason : " رد شد: " + s.ReturnReason), cancellationToken);
            return await GetSummaryAsync(s.Id, cancellationToken);
        }
    }
}
