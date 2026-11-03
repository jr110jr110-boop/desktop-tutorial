using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcReportService
    {
        Task<ResponseDto<PagedList<PcShipmentDto>>> GetShipmentsAsync(PcShipmentQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcShipmentDto>> SaveShipmentAsync(PcShipmentDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcShipmentDto>> ShipmentActionAsync(PcShipmentActionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PcReportDto>> GenerateReportAsync(PcReportRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcReportDto>> ReleaseReportAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcReportDto>>> GetReportsAsync(PcReportQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcReportDto>> GetReportAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PagedList<PcPortfolioRowDto>>> GetPortfolioAsync(PaginationParams query, CancellationToken cancellationToken);
        Task<PcDashboardDto> GetDashboardAsync(CancellationToken cancellationToken);
    }

    /// <summary>Shipping (the planning-owned release gate), the three periodic reports and the portfolio view for the CEO dashboard.</summary>
    public class PcReportService : IPcReportService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IPcProgressService _progress;
        private readonly IPcCommercialService _commercial;
        private readonly IPcProjectService _project;
        private readonly IPcMdrService _mdr;
        private readonly IPcBaselineService _baseline;

        public PcReportService(IUnitOfWork uow, IInventoryUserContext user, IPcProgressService progress, IPcCommercialService commercial, IPcProjectService project, IPcMdrService mdr, IPcBaselineService baseline)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _commercial = commercial ?? throw new ArgumentNullException(nameof(commercial));
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _mdr = mdr ?? throw new ArgumentNullException(nameof(mdr));
            _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);

        // ============================================================================ shipments

        private static PcShipmentDto ToDto(PcShipment s, string woCode) => new PcShipmentDto
        {
            Id = s.Id, Number = s.Number, ContractId = s.ContractId, WorkOrderId = s.WorkOrderId, WorkOrderCode = woCode, ReleaseNoteId = s.ReleaseNoteId, Status = s.Status, PackingList = s.PackingList,
            PackingVerification = s.PackingVerification, PackingPhotoDocumentId = s.PackingPhotoDocumentId, ShippingDocuments = s.ShippingDocuments, Waybill = s.Waybill, ShippedAt = s.ShippedAt,
            DeliveryReceiptNumber = s.DeliveryReceiptNumber, DeliveredAt = s.DeliveredAt, SignedReceiptDocumentId = s.SignedReceiptDocumentId, HandoverMinutesNumber = s.HandoverMinutesNumber, HandoverAt = s.HandoverAt, CommissionedAt = s.CommissionedAt,
            Lines = (s.Lines ?? new List<PcShipmentLine>()).OrderBy(l => l.Id).Select(l => new PcShipmentLineDto { ItemId = l.ItemId, SerialUnitId = l.SerialUnitId, Quantity = l.Quantity, Description = l.Description }).ToList(),
            InventoryDocumentId = s.InventoryDocumentId, PackedAt = s.PackedAt, PackedByUserId = s.PackedByUserId, PackingCheckedByUserId = s.PackingCheckedByUserId, PackingCheckedAt = s.PackingCheckedAt,
            RowVersion = s.RowVersion == null ? null : Convert.ToBase64String(s.RowVersion),
        };

        public async Task<ResponseDto<PagedList<PcShipmentDto>>> GetShipmentsAsync(PcShipmentQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcShipmentQuery();
            IQueryable<PcShipment> q = _uow.Set<PcShipment>().AsNoTracking().Include(s => s.WorkOrder).Include(s => s.Lines).AsSplitQuery().Where(s => !s.Deleted);
            if (query.ContractId.HasValue) q = q.Where(s => s.ContractId == query.ContractId.Value);
            if (query.WorkOrderId.HasValue) q = q.Where(s => s.WorkOrderId == query.WorkOrderId.Value);
            if (query.Status.HasValue) q = q.Where(s => s.Status == query.Status.Value);
            PagedList<PcShipment> page = await PagedList<PcShipment>.CreateAsync(q.OrderByDescending(s => s.Id), query.PageNumber, query.PageSize, cancellationToken);
            List<PcShipmentDto> dtos = page.Select(s => ToDto(s, s.WorkOrder?.Code)).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcShipmentDto>)new PcPage<PcShipmentDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        /// <summary>
        /// R1c (M8): the lines of a shipment (nothing saved). A serial unit is one piece of this work order that is on no other shipment; the delivered quantities of the work order never exceed the ordered quantity.
        /// Returns the Persian error, or null.
        /// </summary>
        private async Task<(List<PcShipmentLine> Lines, string Error)> BuildLinesAsync(WorkOrder wo, int? shipmentId, IList<PcShipmentLineDto> input, CancellationToken cancellationToken)
        {
            var lines = new List<PcShipmentLine>();
            var serials = new HashSet<int>();
            foreach (PcShipmentLineDto l in input)
            {
                if (l.Quantity <= 0m || l.Quantity > 1000000m) return (null, "تعداد هر ردیف محموله باید بیشتر از صفر باشد.");
                if (!l.ItemId.HasValue && !l.SerialUnitId.HasValue && Clean(l.Description) == null) return (null, "هر ردیف محموله کالا، شماره سریال یا شرح دارد.");
                if (l.SerialUnitId.HasValue)
                {
                    if (l.Quantity != 1m) return (null, "ردیف دارای شماره سریال یک دستگاه است (تعداد ۱).");
                    if (!serials.Add(l.SerialUnitId.Value)) return (null, "شماره سریال تکراری است.");
                }
                lines.Add(new PcShipmentLine { ItemId = l.ItemId, SerialUnitId = l.SerialUnitId, Quantity = l.Quantity, Description = Clean(l.Description) });
            }
            List<int> itemIds = lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value).Distinct().ToList();
            if (itemIds.Count > 0 && await _uow.Set<InventoryItem>().AsNoTracking().CountAsync(i => itemIds.Contains(i.Id) && !i.Deleted, cancellationToken) != itemIds.Count) return (null, "کالای ردیف محموله پیدا نشد.");
            if (itemIds.Count > 0)
            {
                // R1c (L3): an item of a shipment line belongs to the work order: it is an item of one of its serial units or of a document of the warehouse for it
                HashSet<int> own = (await _uow.Set<SerialUnit>().AsNoTracking().Where(u => u.WorkOrderId == wo.Id && !u.Deleted).Select(u => u.ItemId).ToListAsync(cancellationToken))
                    .Concat(await _uow.Set<InventoryDocumentLine>().AsNoTracking().Where(x => x.Document.WorkOrderId == wo.Id && !x.Document.Deleted).Select(x => x.ItemId).ToListAsync(cancellationToken)).ToHashSet();
                if (itemIds.Any(id => !own.Contains(id))) return (null, "کالای ردیف محموله به این دستور کار تعلق ندارد (نه سریال و نه سند انبار این دستور کار آن را دارد).");
            }
            if (serials.Count > 0)
            {
                if (await _uow.Set<SerialUnit>().AsNoTracking().CountAsync(u => serials.Contains(u.Id) && u.WorkOrderId == wo.Id && !u.Deleted, cancellationToken) != serials.Count) return (null, "شماره سریال متعلق به این دستور کار نیست.");
                int self = shipmentId ?? 0;
                if (await _uow.Set<PcShipmentLine>().AsNoTracking().AnyAsync(x => x.SerialUnitId != null && serials.Contains(x.SerialUnitId.Value) && !x.Shipment.Deleted && x.ShipmentId != self, cancellationToken))
                    return (null, "شماره سریال در محموله دیگری آمده است.");
            }
            int other = shipmentId ?? 0;
            decimal shippedElsewhere = await _uow.Set<PcShipmentLine>().AsNoTracking().Where(x => x.Shipment.WorkOrderId == wo.Id && !x.Shipment.Deleted && x.ShipmentId != other).SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0m;
            decimal ordered = wo.Count > 0 ? wo.Count : 1m;
            if (shippedElsewhere + lines.Sum(l => l.Quantity) > ordered) return (null, "مجموع تعداد محموله‌های این دستور کار از تعداد سفارش (" + ordered.ToString("0.####", CultureInfo.InvariantCulture) + ") بیشتر می‌شود.");
            return (lines, null);
        }

        public async Task<ResponseDto<PcShipmentDto>> SaveShipmentAsync(PcShipmentDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcShipmentDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning + ",WAREHOUSE,WHSUPERVISOR")) return Bad<PcShipmentDto>(PcText.NotAllowed);
            WorkOrder wo = await _uow.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == dto.WorkOrderId && w.ContractId == dto.ContractId && !w.Deleted, cancellationToken);
            if (wo == null) return PcText.NotFound<PcShipmentDto>(PcText.WorkOrderNotFound);
            DateTimeOffset now = DateTimeOffset.Now;
            PcShipment s;
            if (dto.Id.HasValue)
            {
                s = await _uow.Set<PcShipment>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id.Value && !x.Deleted, cancellationToken);
                if (s == null) return PcText.NotFound<PcShipmentDto>("محموله پیدا نشد.");
                if (s.Status != PcShipmentStatus.Planned) return Bad<PcShipmentDto>("فقط محموله برنامه‌ریزی‌شده ویرایش می‌شود.");
                if (s.WorkOrderId != wo.Id) return Bad<PcShipmentDto>(PcText.InvalidData);
            }
            else
            {
                s = new PcShipment { ContractId = dto.ContractId, WorkOrderId = wo.Id, Status = PcShipmentStatus.Planned, CreateDate = now, CreatorUserId = Me, Lines = new List<PcShipmentLine>(), Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Shipment, "SH", DateTime.Today, cancellationToken) };
                _uow.Set<PcShipment>().Add(s);
            }
            // R1c (M8): the list sent replaces the lines; a request without lines keeps the lines it has
            if (dto.Lines != null && dto.Lines.Count > 0)
            {
                var built = await BuildLinesAsync(wo, dto.Id, dto.Lines, cancellationToken);
                if (built.Error != null) return Bad<PcShipmentDto>(built.Error);
                if (s.Lines.Count > 0) _uow.Set<PcShipmentLine>().RemoveRange(s.Lines.ToList());
                s.Lines.Clear();
                foreach (PcShipmentLine line in built.Lines) s.Lines.Add(line);
            }
            s.PackingList = Clean(dto.PackingList); s.ModifyDate = now; s.ModifierUserId = Me;
            DropPortfolioRows();
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcShipmentDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(s, wo.Code));
        }

        public async Task<ResponseDto<PcShipmentDto>> ShipmentActionAsync(PcShipmentActionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || Clean(dto.Action) == null) return Bad<PcShipmentDto>(PcText.InvalidData);
            PcShipment s = await _uow.Set<PcShipment>().Include(x => x.WorkOrder).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (s == null) return PcText.NotFound<PcShipmentDto>("محموله پیدا نشد.");
            DateTime today = DateTime.Today;
            DateTime date = (dto.Date ?? today).Date;
            // R1c (M8): what happened is dated; the future is not a date of a fact
            if (date > today) return Bad<PcShipmentDto>("تاریخ در آینده است؛ فقط کاری ثبت می‌شود که انجام شده است.");
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "pack":
                {
                    if (!_user.IsInRole(ProjectControlRoles.Planning + ",WAREHOUSE,WHSUPERVISOR")) return Bad<PcShipmentDto>(PcText.NotAllowed);
                    if (s.Status != PcShipmentStatus.Planned) return Bad<PcShipmentDto>("محموله قبلاً بسته‌بندی شده است.");
                    // the shipping gate: packing starts only on a released quality release note
                    var note = await _uow.Set<ReleaseNote>().AsNoTracking().Where(r => r.WorkOrderId == s.WorkOrderId && !r.Deleted && r.Status == ReleaseNoteStatus.Released).OrderByDescending(r => r.Id)
                        .Select(r => new { r.Id, r.ReleasedAt }).FirstOrDefaultAsync(cancellationToken);
                    if (note == null) return Bad<PcShipmentDto>("شروع بسته‌بندی بدون برگه ترخیص کیفی صادرشده ممکن نیست (کنترل ترخیص).");
                    if (Clean(dto.PackingList) == null && Clean(s.PackingList) == null) return Bad<PcShipmentDto>("فهرست بسته‌بندی را وارد کنید.");
                    if (Clean(dto.PackingVerification) == null) return Bad<PcShipmentDto>("نتیجه بازبینی بسته‌بندی را وارد کنید.");
                    if (s.Lines == null || s.Lines.Count == 0) return Bad<PcShipmentDto>("ابتدا اقلام محموله (کالا یا شماره سریال و تعداد) را ثبت کنید.");
                    if (note.ReleasedAt.HasValue && date < note.ReleasedAt.Value.Date) return Bad<PcShipmentDto>("تاریخ بسته‌بندی پیش از تاریخ ترخیص کیفی (" + global::Services.NasbNiro.Finance.FiscalCalendars.Text(note.ReleasedAt.Value.Date) + ") است.");
                    s.ReleaseNoteId = note.Id; s.PackingList = Clean(dto.PackingList) ?? s.PackingList; s.PackingVerification = Clean(dto.PackingVerification); s.PackingPhotoDocumentId = dto.PackingPhotoDocumentId; s.Status = PcShipmentStatus.Packed;
                    s.PackedAt = date; s.PackedByUserId = Me;
                    break;
                }
                case "check-packing":
                    // R1c (M8): quality control checks the packing, and not the person who packed it
                    if (!_user.IsInRole("QC,QA")) return Bad<PcShipmentDto>(PcText.NotAllowed + " (بازبینی بسته‌بندی با کنترل کیفیت است)");
                    if (s.Status != PcShipmentStatus.Packed) return Bad<PcShipmentDto>("بازبینی فقط برای محموله بسته‌بندی‌شده ممکن است.");
                    if (s.PackingCheckedByUserId.HasValue) return Bad<PcShipmentDto>("بازبینی بسته‌بندی قبلاً ثبت شده است.");
                    if (s.PackedByUserId == Me) return Bad<PcShipmentDto>(PcText.SameUser + " (بسته‌بندی را شخص دیگری بازبینی کند)");
                    s.PackingCheckedByUserId = Me; s.PackingCheckedAt = now;
                    break;
                case "release":
                    if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcShipmentDto>(PcText.NotAllowed + " (مجوز ارسال با برنامه‌ریزی است)");
                    if (s.Status != PcShipmentStatus.Packed) return Bad<PcShipmentDto>("ابتدا بسته‌بندی کامل شود.");
                    if (!s.PackingCheckedByUserId.HasValue) return Bad<PcShipmentDto>("بازبینی بسته‌بندی توسط کنترل کیفیت لازم است (کنترل ترخیص).");
                    if (s.PackedByUserId == Me) return Bad<PcShipmentDto>(PcText.SameUser + " (بسته‌بند محموله مجوز ارسال آن را نمی‌دهد)");
                    if (!await _uow.Set<ReleaseNote>().AnyAsync(r => r.WorkOrderId == s.WorkOrderId && !r.Deleted && r.Status == ReleaseNoteStatus.Released, cancellationToken)) return Bad<PcShipmentDto>("برگه ترخیص کیفی دیگر معتبر نیست.");
                    s.Status = PcShipmentStatus.Released;
                    break;
                case "ship":
                {
                    if (!_user.IsInRole(ProjectControlRoles.Planning + ",WAREHOUSE,WHSUPERVISOR")) return Bad<PcShipmentDto>(PcText.NotAllowed);
                    if (s.Status != PcShipmentStatus.Released) return Bad<PcShipmentDto>("ارسال نیازمند مجوز ارسال برنامه‌ریزی است.");
                    if (Clean(dto.Waybill) == null || Clean(dto.ShippingDocuments) == null) return Bad<PcShipmentDto>("بارنامه و اسناد حمل را وارد کنید.");
                    if (s.PackedAt.HasValue && date < s.PackedAt.Value.Date) return Bad<PcShipmentDto>("تاریخ ارسال پیش از تاریخ بسته‌بندی است.");
                    // R1c (M8): the shipment is tied to the confirmed customer-delivery document of the warehouse (the stock really left)
                    if (!dto.InventoryDocumentId.HasValue) return Bad<PcShipmentDto>("سند «تحویل به مشتری» تاییدشده انبار را برای محموله انتخاب کنید.");
                    var doc = await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.Id == dto.InventoryDocumentId.Value && !d.Deleted).Select(d => new { d.Id, d.Type, d.Status, d.ContractId, d.WorkOrderId }).FirstOrDefaultAsync(cancellationToken);
                    if (doc == null || doc.Type != InventoryDocumentType.CustomerDelivery || doc.Status != InventoryDocumentStatus.Confirmed) return Bad<PcShipmentDto>("سند انبار باید «تحویل به مشتری» تاییدشده باشد.");
                    if (doc.ContractId != s.ContractId || (doc.WorkOrderId.HasValue && doc.WorkOrderId.Value != s.WorkOrderId)) return Bad<PcShipmentDto>("سند انبار متعلق به این قرارداد و دستور کار نیست.");
                    if (await _uow.Set<PcShipment>().AnyAsync(x => x.InventoryDocumentId == doc.Id && x.Id != s.Id && !x.Deleted, cancellationToken)) return Bad<PcShipmentDto>("این سند انبار به محموله دیگری وصل شده است.");
                    s.InventoryDocumentId = doc.Id;
                    s.Waybill = Clean(dto.Waybill); s.ShippingDocuments = Clean(dto.ShippingDocuments); s.ShippedAt = date; s.Status = PcShipmentStatus.Shipped;
                    break;
                }
                case "deliver":
                    if (!_user.IsInRole(ProjectControlRoles.Planning + ",COMMERCIAL,WAREHOUSE,WHSUPERVISOR")) return Bad<PcShipmentDto>(PcText.NotAllowed);
                    if (s.Status != PcShipmentStatus.Shipped) return Bad<PcShipmentDto>("محموله هنوز ارسال نشده است.");
                    if (Clean(dto.DeliveryReceiptNumber) == null) return Bad<PcShipmentDto>("شماره رسید تحویل را وارد کنید.");
                    // R1c (M8): delivered means the client signed the receipt, so the signed copy is attached before the status changes
                    if (!dto.SignedReceiptDocumentId.HasValue) return Bad<PcShipmentDto>("رسید امضاشده تحویل (پیوست) لازم است؛ تحویل بدون رسید امضاشده ثبت نمی‌شود.");
                    if (s.ShippedAt.HasValue && date < s.ShippedAt.Value.Date) return Bad<PcShipmentDto>("تاریخ تحویل پیش از تاریخ ارسال است.");
                    s.DeliveryReceiptNumber = Clean(dto.DeliveryReceiptNumber); s.SignedReceiptDocumentId = dto.SignedReceiptDocumentId; s.DeliveredAt = date; s.Status = PcShipmentStatus.Delivered;
                    break;
                case "handover":
                    if (!_user.IsInRole(ProjectControlRoles.Planning + ",COMMERCIAL")) return Bad<PcShipmentDto>(PcText.NotAllowed);
                    if (s.Status != PcShipmentStatus.Delivered) return Bad<PcShipmentDto>("محموله هنوز تحویل نشده است.");
                    if (Clean(dto.HandoverMinutesNumber) == null) return Bad<PcShipmentDto>("شماره صورتجلسه تحویل را وارد کنید.");
                    if (s.DeliveredAt.HasValue && date < s.DeliveredAt.Value.Date) return Bad<PcShipmentDto>("تاریخ صورتجلسه تحویل پیش از تاریخ تحویل است.");
                    s.HandoverMinutesNumber = Clean(dto.HandoverMinutesNumber); s.HandoverAt = date; s.Status = PcShipmentStatus.HandoverReceived;
                    // after-sales: the handover creates the draft of the installed unit (service confirms the site, the tag and the warranty); saved together with the shipment
                    await AfterSales.SvUnitDraft.DraftAsync(_uow, s, Me, cancellationToken);
                    break;
                case "commission":
                    // R1c (M5): the commissioning date is recorded on its own (it is usually later than the handover) and decides the "months after commissioning" of the warranty
                    if (!_user.IsInRole(ProjectControlRoles.Planning + ",COMMERCIAL,SERVICE")) return Bad<PcShipmentDto>(PcText.NotAllowed);
                    if (s.Status != PcShipmentStatus.HandoverReceived) return Bad<PcShipmentDto>("تاریخ راه‌اندازی پس از صورتجلسه تحویل ثبت می‌شود.");
                    if (s.CommissionedAt.HasValue) return Bad<PcShipmentDto>("تاریخ راه‌اندازی قبلاً ثبت شده است.");
                    if (s.HandoverAt.HasValue && date < s.HandoverAt.Value.Date) return Bad<PcShipmentDto>("تاریخ راه‌اندازی پیش از تاریخ صورتجلسه تحویل است.");
                    s.CommissionedAt = date;
                    await AfterSales.SvUnitDraft.RefreshWarrantyAsync(_uow, s, cancellationToken);
                    break;
                default:
                    return Bad<PcShipmentDto>("عملیات نامعتبر است.");
            }
            s.ModifyDate = now; s.ModifierUserId = Me;
            DropPortfolioRows();
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcShipmentDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(s, s.WorkOrder?.Code));
        }

        // ============================================================================ reports

        private static PcReportDto ToDto(PcReportIssue r, bool withContent) => new PcReportDto
        {
            Id = r.Id, Number = r.Number, Kind = r.Kind, ContractId = r.ContractId, DataDate = r.DataDate, GeneratedAt = r.GeneratedAt, Hash = r.Hash, ReleasedAt = r.ReleasedAt, Content = withContent ? r.Content : null,
        };

        public async Task<ResponseDto<PcReportDto>> GenerateReportAsync(PcReportRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcReportDto>(PcText.InvalidData);
            if (dto.Kind != PcReportKind.ClientMonthly && dto.Kind != PcReportKind.InternalWeekly && dto.Kind != PcReportKind.CeoMonthly) return Bad<PcReportDto>("نوع گزارش نامعتبر است.");
            if (!_user.IsInRole(ProjectControlRoles.PlanningOrPm + ",CEO")) return Bad<PcReportDto>(PcText.NotAllowed);
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.ContractId && !c.Deleted, cancellationToken);
            if (contract == null) return PcText.NotFound<PcReportDto>(PcText.ContractNotFound);
            DateTime asOf = (dto.DataDate ?? DateTime.Today).Date;
            PcContractTerms terms = await PcContext.TermsAsync(_uow, dto.ContractId, false, cancellationToken);
            PcEvmDto evm = await _progress.ComputeEvmAsync(dto.ContractId, asOf, cancellationToken);
            string content;
            string prefix;
            if (dto.Kind == PcReportKind.ClientMonthly)
            {
                prefix = "RPC";
                int submitted = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().CountAsync(d => d.ContractId == dto.ContractId && !d.Deleted && d.ClientSubmittal && d.IssueState != PcIssueState.NotIssued, cancellationToken);
                int approved = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().CountAsync(d => d.ContractId == dto.ContractId && !d.Deleted && d.ClientSubmittal && d.IssueState == PcIssueState.Approved, cancellationToken);
                PcProgressDto prog = (await _progress.GetProgressAsync(dto.ContractId, cancellationToken)).Data;
                bool indices = terms != null && terms.ClientReportShowsIndices;
                var c = new PcClientReportContent
                {
                    ProjectCode = terms?.ProjectCode, ContractTitle = contract.Title, DataDate = asOf, PhysicalPercent = evm.PhysicalPercent, PlannedPercent = evm.PlannedPercent, BaselineFinish = evm.BaselineFinish, ForecastFinish = evm.ForecastFinish,
                    Phases = prog?.ByPhase ?? new List<PcPhaseProgressDto>(), DocumentsSubmitted = submitted, DocumentsApproved = approved, DocumentsAwaitingResponse = await _mdr.OverdueResponsesAsync(dto.ContractId, cancellationToken),
                    Spi = indices ? evm.Spi : null, Cpi = indices ? evm.Cpi : null,
                };
                List<PcPaymentMilestone> ms = await _uow.Set<PcPaymentMilestone>().AsNoTracking().Where(m => m.ContractId == dto.ContractId && !m.Deleted).OrderBy(m => m.Id).ToListAsync(cancellationToken);
                c.Milestones = ms.Select(m => $"{m.Title}: {m.Status}").ToList();
                content = JsonSerializer.Serialize(c);
            }
            else if (dto.Kind == PcReportKind.InternalWeekly)
            {
                prefix = "RPW";
                PcProgressDto prog = (await _progress.GetProgressAsync(dto.ContractId, cancellationToken)).Data;
                List<string> late = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == dto.ContractId && !a.Deleted && a.IsLate).OrderBy(a => a.Id).Select(a => a.Code).ToListAsync(cancellationToken);
                int openChanges = await _uow.Set<PcChangeRequest>().AsNoTracking().CountAsync(x => x.ContractId == dto.ContractId && !x.Deleted && x.Status != PcChangeStatus.Closed && x.Status != PcChangeStatus.Rejected && x.Status != PcChangeStatus.Draft, cancellationToken);
                List<PcGuaranteeAlertDto> alerts = await _commercial.GuaranteeAlertsAsync(dto.ContractId, cancellationToken);
                content = JsonSerializer.Serialize(new { Progress = prog, Evm = evm, LateActivities = late, OpenChanges = openChanges, GuaranteeAlerts = alerts.Count });
            }
            else
            {
                prefix = "RPM";
                PcPortfolioRowDto row = await RowAsync(dto.ContractId, contract.Title, terms, asOf, cancellationToken);
                content = JsonSerializer.Serialize(new { Row = row, Evm = evm });
            }
            DateTimeOffset now = DateTimeOffset.Now;
            var issue = new PcReportIssue
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, PcNumbers.Report, prefix, asOf, cancellationToken), Kind = dto.Kind, ContractId = dto.ContractId, DataDate = asOf, GeneratedAt = now, GeneratedByUserId = Me,
                Content = content, Hash = PcText.Sha256(dto.ContractId + "|" + dto.Kind + "|" + asOf.ToString("yyyy-MM-dd") + "|" + content),
            };
            _uow.Set<PcReportIssue>().Add(issue);
            DropPortfolioRows();
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(issue, true));
        }

        public async Task<ResponseDto<PcReportDto>> ReleaseReportAsync(int id, CancellationToken cancellationToken)
        {
            if (!_user.IsInRole(ProjectControlRoles.ProjectManagers)) return Bad<PcReportDto>(PcText.NotAllowed + " (انتشار گزارش با مدیر پروژه است)");
            PcReportIssue r = await _uow.Set<PcReportIssue>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (r == null) return PcText.NotFound<PcReportDto>("گزارش پیدا نشد.");
            if (r.ReleasedAt.HasValue) return Bad<PcReportDto>("گزارش قبلاً منتشر شده است.");
            // R1c (M1): the report is released by someone other than the person who generated it
            if (r.GeneratedByUserId == Me) return Bad<PcReportDto>(PcText.SameUser + " (تولیدکننده گزارش آن را منتشر نمی‌کند)");
            r.ReleasedAt = DateTimeOffset.Now; r.ReleasedByUserId = Me;
            DropPortfolioRows();
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(r, true));
        }

        public async Task<ResponseDto<PagedList<PcReportDto>>> GetReportsAsync(PcReportQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcReportQuery();
            IQueryable<PcReportIssue> q = _uow.Set<PcReportIssue>().AsNoTracking().Where(r => r.Kind != PcReportKind.FinalBookCompany && r.Kind != PcReportKind.FinalBookClient);
            if (query.ContractId.HasValue) q = q.Where(r => r.ContractId == query.ContractId.Value);
            if (query.Kind.HasValue) q = q.Where(r => r.Kind == query.Kind.Value);
            IQueryable<PcReportDto> proj = q.OrderByDescending(r => r.Id).Select(r => new PcReportDto
                { Id = r.Id, Number = r.Number, Kind = r.Kind, ContractId = r.ContractId, DataDate = r.DataDate, GeneratedAt = r.GeneratedAt, Hash = r.Hash, ReleasedAt = r.ReleasedAt });
            return ResponseDtoHelper.Ok(await PagedList<PcReportDto>.CreateAsync(proj, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcReportDto>> GetReportAsync(int id, CancellationToken cancellationToken)
        {
            PcReportIssue r = await _uow.Set<PcReportIssue>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            return r == null ? PcText.NotFound<PcReportDto>("گزارش پیدا نشد.") : ResponseDtoHelper.Ok(ToDto(r, true));
        }

        // ============================================================================ portfolio and dashboard

        /// <param name="overdueInitiationContracts">The contracts with an overdue initiation step (a query over all the projects): the portfolio asks it once and hands it in; a single row asks for itself.</param>
        private async Task<PcPortfolioRowDto> RowAsync(int contractId, string title, PcContractTerms terms, DateTime asOf, CancellationToken cancellationToken, List<int> overdueInitiationContracts = null)
        {
            PcEvmDto evm = await _progress.ComputeEvmAsync(contractId, asOf, cancellationToken);
            List<PcGuaranteeAlertDto> alerts = await _commercial.GuaranteeAlertsAsync(contractId, cancellationToken);
            List<int> overdueInit = overdueInitiationContracts ?? await _project.OverdueContractIdsAsync(cancellationToken);
            return new PcPortfolioRowDto
            {
                ContractId = contractId, ContractTitle = title, ProjectCode = terms?.ProjectCode, PhysicalPercent = evm.PhysicalPercent, PlannedPercent = evm.PlannedPercent, Spi = evm.Spi, Cpi = evm.Cpi, SpiStatus = evm.SpiStatus, CpiStatus = evm.CpiStatus,
                ForecastFinish = evm.ForecastFinish,
                LateActivities = await _uow.Set<PcActivity>().AsNoTracking().CountAsync(a => a.ContractId == contractId && !a.Deleted && a.IsLate, cancellationToken),
                OpenChanges = await _uow.Set<PcChangeRequest>().AsNoTracking().CountAsync(x => x.ContractId == contractId && !x.Deleted && (x.Status == PcChangeStatus.Registered || x.Status == PcChangeStatus.InAnalysis), cancellationToken),
                GuaranteeAlerts = alerts.Count,
                OverdueMilestones = await _uow.Set<PcPaymentMilestone>().AsNoTracking().CountAsync(m => m.ContractId == contractId && !m.Deleted && m.Status == PcMilestoneStatus.Planned && m.PlannedDate != null && m.PlannedDate < asOf, cancellationToken),
                OverdueInitiationSteps = overdueInit.Count(i => i == contractId),
                OverdueClientResponses = await _mdr.OverdueResponsesAsync(contractId, cancellationToken),
                PenaltyExposure = await _uow.Set<PcPenaltyAssessment>().AsNoTracking().Where(p => p.ContractId == contractId && !p.Deleted && p.Status == PcPenaltyStatus.Computed).SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m,
            };
        }

        // The rows of the whole portfolio are computed project by project (earned value, late activities, guarantees ...). The CEO dashboard and the CEO overview both ask for them
        // inside one request, so this service instance keeps the last answer for a few seconds. It keeps its own rows and hands out copies: a caller that changes what it was given
        // (a sort, a mask, a corrected field) changes nothing for the next reader, and the rows are dropped whenever this instance saves anything.
        private const long PortfolioRowsLifetimeMs = 10_000;
        private List<PcPortfolioRowDto> _portfolioRows;
        private long _portfolioRowsAt;
        private DateTime _portfolioRowsDay;

        private void DropPortfolioRows() => _portfolioRows = null;

        private static PcPortfolioRowDto Copy(PcPortfolioRowDto r) => new PcPortfolioRowDto
        {
            ContractId = r.ContractId, ContractTitle = r.ContractTitle, ProjectCode = r.ProjectCode, PhysicalPercent = r.PhysicalPercent, PlannedPercent = r.PlannedPercent, Spi = r.Spi, Cpi = r.Cpi,
            SpiStatus = r.SpiStatus, CpiStatus = r.CpiStatus, ForecastFinish = r.ForecastFinish, LateActivities = r.LateActivities, OpenChanges = r.OpenChanges, GuaranteeAlerts = r.GuaranteeAlerts,
            OverdueMilestones = r.OverdueMilestones, OverdueInitiationSteps = r.OverdueInitiationSteps, OverdueClientResponses = r.OverdueClientResponses, PenaltyExposure = r.PenaltyExposure,
        };

        /// <summary>A project is red when one of its two indexes (schedule, cost) is red.</summary>
        public static bool IsRed(PcPortfolioRowDto r) => r.SpiStatus == "red" || r.CpiStatus == "red";

        /// <summary>A project is amber when one of its indexes is amber and none is red: each project has one colour, so red + amber + green is the number of projects.</summary>
        public static bool IsAmber(PcPortfolioRowDto r) => !IsRed(r) && (r.SpiStatus == "amber" || r.CpiStatus == "amber");

        private async Task<List<PcPortfolioRowDto>> AllRowsAsync(CancellationToken cancellationToken)
        {
            if (_portfolioRows != null && _portfolioRowsDay == DateTime.Today && Environment.TickCount64 - _portfolioRowsAt < PortfolioRowsLifetimeMs) return _portfolioRows.Select(Copy).ToList();
            List<int> ids = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.Contract.Deleted == false).OrderBy(t => t.ContractId).Select(t => t.ContractId).ToListAsync(cancellationToken);
            var rows = new List<PcPortfolioRowDto>();
            // one query for all the projects, not one per project
            List<int> overdueInit = ids.Count == 0 ? new List<int>() : await _project.OverdueContractIdsAsync(cancellationToken);
            foreach (int id in ids)
            {
                var c = await _uow.Set<ContractEntity>().AsNoTracking().Where(x => x.Id == id).Select(x => x.Title).FirstOrDefaultAsync(cancellationToken);
                PcContractTerms terms = await PcContext.TermsAsync(_uow, id, false, cancellationToken);
                rows.Add(await RowAsync(id, c, terms, DateTime.Today, cancellationToken, overdueInit));
            }
            _portfolioRows = rows.Select(Copy).ToList();
            _portfolioRowsDay = DateTime.Today;
            _portfolioRowsAt = Environment.TickCount64;
            return rows;
        }

        public async Task<ResponseDto<PagedList<PcPortfolioRowDto>>> GetPortfolioAsync(PaginationParams query, CancellationToken cancellationToken)
        {
            query ??= new PaginationParams();
            List<PcPortfolioRowDto> rows = await AllRowsAsync(cancellationToken);
            int size = query.PageSize <= 0 ? Math.Max(1, rows.Count) : query.PageSize;
            int number = Math.Max(1, query.PageNumber);
            List<PcPortfolioRowDto> slice = rows.Skip((number - 1) * size).Take(size).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcPortfolioRowDto>)new PcPage<PcPortfolioRowDto>(slice, rows.Count, number, size));
        }

        public async Task<PcDashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
        {
            List<PcPortfolioRowDto> rows = await AllRowsAsync(cancellationToken);
            return new PcDashboardDto
            {
                ActiveProjects = rows.Count, ProjectsRed = rows.Count(IsRed), ProjectsAmber = rows.Count(IsAmber),
                LateActivities = rows.Sum(r => r.LateActivities), OpenChangeRequests = rows.Sum(r => r.OpenChanges), OverdueChangeRequests = (await _baseline.OverdueChangeContractIdsAsync(cancellationToken)).Count,
                GuaranteeAlerts = rows.Sum(r => r.GuaranteeAlerts), OverdueMilestones = rows.Sum(r => r.OverdueMilestones), OverdueInitiationSteps = rows.Sum(r => r.OverdueInitiationSteps),
                OverdueClientResponses = rows.Sum(r => r.OverdueClientResponses), PenaltyExposure = rows.Sum(r => r.PenaltyExposure),
                Worst = rows.OrderBy(r => r.Spi ?? 9m).Take(5).ToList(),
            };
        }
    }
}
