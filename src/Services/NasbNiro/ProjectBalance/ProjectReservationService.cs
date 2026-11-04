using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectBalance;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace Services.NasbNiro.ProjectBalance
{
    public interface IProjectReservationService
    {
        Task<ResponseDto<List<ReservationDto>>> GetAsync(ReservationQuery query, CancellationToken ct);
        Task<ResponseDto<ReservationDto>> UpdateAsync(UpdateReservationDto dto, CancellationToken ct);
        Task<ResponseDto<ReservationDto>> RequestReleaseAsync(ReservationActionDto dto, CancellationToken ct);
        Task<ResponseDto<ReservationDto>> DecideReleaseAsync(ReservationActionDto dto, CancellationToken ct);
        Task<ResponseDto<ReservationDto>> ReEnableAsync(ReservationActionDto dto, CancellationToken ct);
    }

    /// <summary>
    /// Stock held for a project. PLANNING edits a reservation with a reason (the audit trail keeps the before and after); releasing it is a decision of two people (one asks, another approves);
    /// issuing the stock to the project uses it up. AI never approves a release.
    /// </summary>
    public sealed class ProjectReservationService : PbServiceBase, IProjectReservationService
    {
        public ProjectReservationService(IUnitOfWork uow, IInventoryUserContext user) : base(uow, user)
        {
        }

        private static string Cut(string text, int max) => text == null ? null : text.Length <= max ? text : text.Substring(0, max);

        private static decimal Remaining(ProjectReservation r) => r.Status == ReservationStatus.Active ? Math.Max(0m, r.Quantity - r.IssuedQuantity) : 0m;

        public async Task<ResponseDto<List<ReservationDto>>> GetAsync(ReservationQuery query, CancellationToken ct)
        {
            query ??= new ReservationQuery();
            IQueryable<ProjectReservation> q = Uow.Set<ProjectReservation>().AsNoTracking().Where(r => !r.Deleted);
            if (query.WorkOrderId.HasValue) q = q.Where(r => r.WorkOrderId == query.WorkOrderId.Value);
            if (query.ContractId.HasValue) q = q.Where(r => r.ContractId == query.ContractId.Value);
            if (query.ItemId.HasValue) q = q.Where(r => r.ItemId == query.ItemId.Value);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            List<ProjectReservation> rows = await q.OrderByDescending(r => r.Id).Take(1000).ToListAsync(ct);
            return ResponseDtoHelper.Ok(await DtosAsync(rows, query.Contains, ct));
        }

        private async Task<List<ReservationDto>> DtosAsync(List<ProjectReservation> rows, string contains, CancellationToken ct)
        {
            List<int> itemIds = rows.Select(r => r.ItemId).Concat(rows.Where(r => r.SubstituteForItemId.HasValue).Select(r => r.SubstituteForItemId.Value)).Distinct().ToList();
            var items = itemIds.Count == 0 ? new Dictionary<int, (string Code, string Title, string Unit)>() : (await Uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title, i.Unit }).ToListAsync(ct)).ToDictionary(i => i.Id, i => (i.Code, i.Title, i.Unit));
            List<int> woIds = rows.Select(r => r.WorkOrderId).Distinct().ToList();
            var orders = woIds.Count == 0 ? new Dictionary<int, (string Code, string Contract)>() : (await Uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).Select(w => new { w.Id, w.Code, Contract = w.Contract.Code }).ToListAsync(ct)).ToDictionary(w => w.Id, w => (w.Code, w.Contract));
            List<int> lotIds = rows.Where(r => r.LotId.HasValue).Select(r => r.LotId.Value).Distinct().ToList();
            Dictionary<int, string> lots = lotIds.Count == 0 ? new Dictionary<int, string>() : (await Uow.Set<InventoryLot>().AsNoTracking().Where(l => lotIds.Contains(l.Id)).Select(l => new { l.Id, l.LotNumber }).ToListAsync(ct)).ToDictionary(l => l.Id, l => l.LotNumber);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(Uow, rows.Select(r => r.ReleaseRequestedByUserId), ct);
            int me = User.UserId;
            bool planner = InRole(BalanceRoles.Planning) && !IsAi;
            var list = rows.Select(r =>
            {
                items.TryGetValue(r.ItemId, out var it);
                orders.TryGetValue(r.WorkOrderId, out var wo);
                bool pending = r.ReleaseRequestedAt.HasValue && r.Status == ReservationStatus.Active;
                return new ReservationDto
                {
                    Id = r.Id, ContractId = r.ContractId, ContractCode = wo.Contract, WorkOrderId = r.WorkOrderId, WorkOrderCode = wo.Code, BaselineId = r.BaselineId, ItemId = r.ItemId, ItemCode = it.Code, ItemTitle = it.Title, Unit = it.Unit,
                    Quantity = r.Quantity, IssuedQuantity = r.IssuedQuantity, RemainingQuantity = Remaining(r), Status = r.Status, IsHard = r.IsHard, WarehouseId = r.WarehouseId, LotId = r.LotId,
                    LotNumber = r.LotId.HasValue && lots.TryGetValue(r.LotId.Value, out string ln) ? ln : null, SerialUnitId = r.SerialUnitId, NeedDate = r.NeedDate, WbsNodeId = r.WbsNodeId, ActivityId = r.ActivityId,
                    SubstituteForItemId = r.SubstituteForItemId, SubstituteForItemCode = r.SubstituteForItemId.HasValue && items.TryGetValue(r.SubstituteForItemId.Value, out var sf) ? sf.Code : null, Reason = r.Reason,
                    ReleaseRequestedByUserId = r.ReleaseRequestedByUserId, ReleaseRequestedByName = names.Name(r.ReleaseRequestedByUserId), ReleaseRequestedAt = r.ReleaseRequestedAt, ReleaseReason = r.ReleaseReason,
                    ReleasedAt = r.ReleasedAt, ReleasedQuantity = r.ReleasedQuantity, CreateDate = r.CreateDate,
                    CanEdit = planner && r.Status == ReservationStatus.Active && !pending,
                    CanRequestRelease = planner && r.Status == ReservationStatus.Active && !pending,
                    CanApproveRelease = planner && pending && r.ReleaseRequestedByUserId != me,
                    AutoReserveBlocked = r.AutoReserveBlocked, ReEnableReason = r.ReEnableReason,
                    CanReEnable = planner && r.Status == ReservationStatus.Released && r.AutoReserveBlocked,
                };
            }).ToList();
            string text = string.IsNullOrWhiteSpace(contains) ? null : contains.Trim();
            if (text != null) list = list.Where(r => (r.ItemCode ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (r.ItemTitle ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (r.WorkOrderCode ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            return list;
        }

        private async Task<ReservationDto> OneAsync(int id, CancellationToken ct) =>
            (await DtosAsync(await Uow.Set<ProjectReservation>().AsNoTracking().Where(r => r.Id == id).ToListAsync(ct), null, ct)).First();

        public async Task<ResponseDto<ReservationDto>> UpdateAsync(UpdateReservationDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.Planning)) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.NotAllowed);
            if (IsAi) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.AiNeverApproves);
            if (dto == null) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.InvalidData);
            string reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            if (reason == null) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.ReasonRequired);
            ProjectReservation r = await Uow.Set<ProjectReservation>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<ReservationDto>(BalanceMessages.NotFound);
            if (r.Status != ReservationStatus.Active) return ResponseDtoHelper.BadRequest<ReservationDto>("فقط رزرو فعال ویرایش می‌شود.");
            if (r.ReleaseRequestedAt.HasValue) return ResponseDtoHelper.BadRequest<ReservationDto>("درخواست آزادسازی این رزرو در انتظار تایید است؛ تا تصمیم گرفته نشده ویرایش نمی‌شود.");

            if (dto.Quantity.HasValue)
            {
                decimal q = BalanceRules.Round(dto.Quantity.Value);
                if (q <= 0) return ResponseDtoHelper.BadRequest<ReservationDto>("مقدار رزرو باید بیشتر از صفر باشد؛ برای برداشتن رزرو، آزادسازی بخواهید.");
                if (q < r.IssuedQuantity) return ResponseDtoHelper.BadRequest<ReservationDto>("مقدار رزرو کمتر از مقدار حواله‌شده به پروژه (" + BalanceRules.Num(r.IssuedQuantity) + ") نمی‌شود.");
                if (q > r.Quantity)
                {
                    BalanceSettings s = await BalanceSettingKeys.LoadAsync(Uow, ct);
                    WarehouseKind[] kinds = s.WarehouseKinds.Select(k => (WarehouseKind)k).ToArray();
                    decimal onHand = await (from sb in Uow.Set<StockBalance>().AsNoTracking() join w in Uow.Set<Warehouse>().AsNoTracking() on sb.WarehouseId equals w.Id
                                            where sb.ItemId == r.ItemId && !w.Deleted && w.IsActive && kinds.Contains(w.Kind) select sb.Quantity).SumAsync(ct);
                    var held = await Uow.Set<ProjectReservation>().AsNoTracking().Where(x => !x.Deleted && x.Status == ReservationStatus.Active && x.ItemId == r.ItemId).Select(x => new { x.Quantity, x.IssuedQuantity }).ToListAsync(ct);
                    decimal free = Math.Max(0m, onHand - held.Sum(x => Math.Max(0m, x.Quantity - x.IssuedQuantity)));
                    if (q - r.Quantity > free) return ResponseDtoHelper.BadRequest<ReservationDto>("موجودی آزاد برای افزایش رزرو کافی نیست؛ آزاد: " + BalanceRules.Num(free) + ".");
                }
                r.Quantity = q;
                if (r.Quantity <= r.IssuedQuantity) r.Status = ReservationStatus.FullyIssued;
            }
            if (dto.IsHard.HasValue) r.IsHard = dto.IsHard.Value;
            if (dto.NeedDate.HasValue) r.NeedDate = dto.NeedDate.Value.Date;
            if (dto.WarehouseId.HasValue)
            {
                if (!await Uow.Set<Warehouse>().AnyAsync(w => w.Id == dto.WarehouseId.Value && !w.Deleted && w.IsActive, ct)) return ResponseDtoHelper.BadRequest<ReservationDto>("انبار فعال پیدا نشد.");
                r.WarehouseId = dto.WarehouseId;
            }
            if (dto.LotId.HasValue)
            {
                if (!await Uow.Set<InventoryLot>().AnyAsync(l => l.Id == dto.LotId.Value && l.ItemId == r.ItemId && !l.Deleted, ct)) return ResponseDtoHelper.BadRequest<ReservationDto>("بچ این کالا پیدا نشد.");
                r.LotId = dto.LotId;
            }
            if (dto.SerialUnitId.HasValue)
            {
                if (!await Uow.Set<SerialUnit>().AnyAsync(u => u.Id == dto.SerialUnitId.Value && u.ItemId == r.ItemId && !u.Deleted, ct)) return ResponseDtoHelper.BadRequest<ReservationDto>("سریال این کالا پیدا نشد.");
                r.SerialUnitId = dto.SerialUnitId;
            }
            r.Reason = Cut(reason, 500);
            Stamp(r, false);
            string saved = await TrySaveAsync(ct);
            if (saved != null) return ResponseDtoHelper.BadRequest<ReservationDto>(saved);
            return ResponseDtoHelper.Ok(await OneAsync(r.Id, ct));
        }

        public async Task<ResponseDto<ReservationDto>> RequestReleaseAsync(ReservationActionDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.Planning)) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.NotAllowed);
            if (IsAi) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.AiNeverApproves);
            string reason = string.IsNullOrWhiteSpace(dto?.Reason) ? null : dto.Reason.Trim();
            if (reason == null) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.ReasonRequired);
            ProjectReservation r = await Uow.Set<ProjectReservation>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<ReservationDto>(BalanceMessages.NotFound);
            if (r.Status != ReservationStatus.Active) return ResponseDtoHelper.BadRequest<ReservationDto>("فقط رزرو فعال آزاد می‌شود.");
            if (r.ReleaseRequestedAt.HasValue) return ResponseDtoHelper.BadRequest<ReservationDto>("درخواست آزادسازی قبلاً ثبت شده است.");
            r.ReleaseRequestedByUserId = User.UserId; r.ReleaseRequestedAt = DateTimeOffset.Now; r.ReleaseReason = Cut(reason, 500);
            Stamp(r, false);
            string saved = await TrySaveAsync(ct);
            if (saved != null) return ResponseDtoHelper.BadRequest<ReservationDto>(saved);
            return ResponseDtoHelper.Ok(await OneAsync(r.Id, ct));
        }

        public async Task<ResponseDto<ReservationDto>> DecideReleaseAsync(ReservationActionDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.Planning)) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.NotAllowed);
            if (IsAi) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.AiNeverApproves);
            if (dto == null) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.InvalidData);
            ProjectReservation r = await Uow.Set<ProjectReservation>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<ReservationDto>(BalanceMessages.NotFound);
            if (r.Status != ReservationStatus.Active || !r.ReleaseRequestedAt.HasValue) return ResponseDtoHelper.BadRequest<ReservationDto>("درخواست آزادسازی در انتظار این رزرو نیست.");
            if (r.ReleaseRequestedByUserId == User.UserId) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.SameUser);
            if (dto.Approve)
            {
                r.ReleasedQuantity = Remaining(r);
                r.Status = ReservationStatus.Released;
                r.ReleasedByUserId = User.UserId; r.ReleasedAt = DateTimeOffset.Now;
                // released by a decision: the balance does not take this stock for this project again by itself until a planner enables it
                r.AutoReserveBlocked = true;
            }
            else
            {
                string note = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
                if (note == null) return ResponseDtoHelper.BadRequest<ReservationDto>("برای رد درخواست آزادسازی دلیل را بنویسید.");
                r.ReleaseRequestedByUserId = null; r.ReleaseRequestedAt = null; r.ReleaseReason = Cut("رد شد: " + note, 500);
            }
            Stamp(r, false);
            string saved = await TrySaveAsync(ct);
            if (saved != null) return ResponseDtoHelper.BadRequest<ReservationDto>(saved);
            return ResponseDtoHelper.Ok(await OneAsync(r.Id, ct));
        }

        /// <summary>A planner lets the balance reserve the item for the project again after a release (a person, with a reason; never an AI seat).</summary>
        public async Task<ResponseDto<ReservationDto>> ReEnableAsync(ReservationActionDto dto, CancellationToken ct)
        {
            if (!InRole(BalanceRoles.Planning)) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.NotAllowed);
            if (IsAi) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.AiNeverApproves);
            string reason = string.IsNullOrWhiteSpace(dto?.Reason) ? null : dto.Reason.Trim();
            if (reason == null) return ResponseDtoHelper.BadRequest<ReservationDto>(BalanceMessages.ReasonRequired);
            ProjectReservation r = await Uow.Set<ProjectReservation>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (r == null) return ResponseDtoHelper.NotFound<ReservationDto>(BalanceMessages.NotFound);
            if (r.Status != ReservationStatus.Released || !r.AutoReserveBlocked) return ResponseDtoHelper.BadRequest<ReservationDto>("فقط رزرو آزادشده که رزرو خودکار آن بسته است دوباره فعال می‌شود.");
            r.AutoReserveBlocked = false; r.ReEnabledByUserId = User.UserId; r.ReEnabledAt = DateTimeOffset.Now; r.ReEnableReason = Cut(reason, 500);
            Stamp(r, false);
            string saved = await TrySaveAsync(ct);
            if (saved != null) return ResponseDtoHelper.BadRequest<ReservationDto>(saved);
            return ResponseDtoHelper.Ok(await OneAsync(r.Id, ct));
        }
    }

    /// <summary>The rules that tie the warehouse documents to the reservations.</summary>
    public static class ProjectReservationRules
    {
        /// <summary>The documents that take stock out of the counted warehouses: an issue, a send to a contractor, a transfer out, a decrease, a stage conversion, a delivery to the customer and a scrap sale.</summary>
        public static bool TakesStockOut(InventoryDocumentType t) =>
            t == InventoryDocumentType.Issue || t == InventoryDocumentType.SubcontractIssue || t == InventoryDocumentType.Transfer || t == InventoryDocumentType.AdjustmentDecrease
            || t == InventoryDocumentType.StageConversion || t == InventoryDocumentType.CustomerDelivery || t == InventoryDocumentType.ScrapSale;

        /// <summary>The documents that hand the stock to a work order and so use up its reservations.</summary>
        public static bool UsesReservation(InventoryDocumentType t) => t == InventoryDocumentType.Issue || t == InventoryDocumentType.SubcontractIssue || t == InventoryDocumentType.Transfer;

        /// <summary>
        /// The work order whose reservations the document uses: the work order of an issue or a send to a contractor; for a transfer (H3, PB5) the work order of the destination warehouse
        /// (a project, project-consumable or point-of-use warehouse), because stock moved there is already with the project.
        /// </summary>
        public static async Task<int?> ReservationWorkOrderAsync(IUnitOfWork uow, InventoryDocument document, CancellationToken ct)
        {
            if (!UsesReservation(document.Type)) return null;
            if (document.Type != InventoryDocumentType.Transfer) return document.WorkOrderId;
            if (!document.DestinationWarehouseId.HasValue) return null;
            return await uow.Set<Warehouse>().AsNoTracking().Where(w => w.Id == document.DestinationWarehouseId.Value).Select(w => w.WorkOrderId).FirstOrDefaultAsync(ct);
        }

        private static IEnumerable<InventoryDocumentLine> OutLines(InventoryDocument document) =>
            document.Lines.Where(l => document.Type != InventoryDocumentType.StageConversion || l.Direction == DocumentLineDirection.Out);

        /// <summary>
        /// A document that takes stock out of the counted warehouses is refused when it would eat into what another project holds under a hard reservation (a reservation of one lot or one serial unit is held for that lot or unit only).
        /// Null when the document may be confirmed. A project may always use its own reservation.
        /// </summary>
        public static async Task<string> CheckIssueAsync(IUnitOfWork uow, InventoryDocument document, CancellationToken ct)
        {
            if (!TakesStockOut(document.Type)) return null;
            BalanceSettings s = await BalanceSettingKeys.LoadAsync(uow, ct);
            WarehouseKind[] kinds = s.WarehouseKinds.Select(k => (WarehouseKind)k).ToArray();
            // a document that moves stock between two counted warehouses (or takes it from a warehouse that is not counted) does not change what is available
            WarehouseKind? source = await uow.Set<Warehouse>().AsNoTracking().Where(w => w.Id == document.WarehouseId).Select(w => (WarehouseKind?)w.Kind).FirstOrDefaultAsync(ct);
            if (source.HasValue && !kinds.Contains(source.Value)) return null;
            if ((document.Type == InventoryDocumentType.Transfer || document.Type == InventoryDocumentType.SubcontractIssue) && document.DestinationWarehouseId.HasValue)
            {
                WarehouseKind? target = await uow.Set<Warehouse>().AsNoTracking().Where(w => w.Id == document.DestinationWarehouseId.Value).Select(w => (WarehouseKind?)w.Kind).FirstOrDefaultAsync(ct);
                if (target.HasValue && kinds.Contains(target.Value)) return null;
            }
            List<InventoryDocumentLine> lines = OutLines(document).ToList();
            if (lines.Count == 0) return null;
            List<int> ids = lines.Select(l => l.ItemId).Distinct().ToList();
            int? workOrderId = await ReservationWorkOrderAsync(uow, document, ct) ?? document.WorkOrderId; // H3: a transfer into the warehouse of a work order may use that work order's own reservation
            var held = await uow.Set<ProjectReservation>().AsNoTracking().Where(r => !r.Deleted && r.Status == ReservationStatus.Active && r.IsHard && ids.Contains(r.ItemId) && (workOrderId == null || r.WorkOrderId != workOrderId.Value))
                .Select(r => new { r.ItemId, r.ContractId, r.LotId, r.SerialUnitId, Left = r.Quantity - r.IssuedQuantity }).ToListAsync(ct);
            held = held.Where(h => h.Left > 0).ToList();
            if (held.Count == 0) return null;
            var stock = await (from sb in uow.Set<StockBalance>().AsNoTracking() join w in uow.Set<Warehouse>().AsNoTracking() on sb.WarehouseId equals w.Id
                               where ids.Contains(sb.ItemId) && !w.Deleted && w.IsActive && kinds.Contains(w.Kind) select new { sb.ItemId, sb.LotId, sb.Quantity }).ToListAsync(ct);
            foreach (int itemId in held.Select(h => h.ItemId).Distinct().OrderBy(x => x))
            {
                var others = held.Where(h => h.ItemId == itemId).ToList();
                List<InventoryDocumentLine> mine = lines.Where(l => l.ItemId == itemId).ToList();
                string Refuse(string why)
                {
                    string code = uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == itemId).Select(i => i.Code).FirstOrDefault();
                    List<int> contracts = others.Select(h => h.ContractId).Distinct().ToList();
                    string projects = string.Join("، ", uow.Set<Entities.Model.NasbNiro.Contracts.ContractEntity>().AsNoTracking().Where(c => contracts.Contains(c.Id)).Select(c => c.Code).ToList());
                    return "کالای " + code + " برای پروژه دیگری رزرو شده است (" + projects + ")؛ " + why + ". حواله بدون آزادسازی تاییدشدهٔ رزرو (برنامه‌ریزی) ممکن نیست.";
                }
                // a serial unit reserved for another project is not given to anybody else
                var unitIds = new HashSet<int>(mine.SelectMany(l => l.Serials ?? new List<InventoryDocumentLineSerial>()).Where(x => x.SerialUnitId.HasValue).Select(x => x.SerialUnitId.Value));
                if (others.Any(h => h.SerialUnitId.HasValue && unitIds.Contains(h.SerialUnitId.Value))) return Refuse("سریال این حواله برای آن پروژه رزرو شده است");
                decimal onHand = stock.Where(x => x.ItemId == itemId).Sum(x => x.Quantity);
                var lotBound = others.Where(h => h.LotId.HasValue && !h.SerialUnitId.HasValue).GroupBy(h => h.LotId.Value).ToDictionary(g => g.Key, g => g.Sum(h => h.Left));
                decimal unbound = others.Where(h => !h.LotId.HasValue && !h.SerialUnitId.HasValue).Sum(h => h.Left);
                decimal wantedTotal = mine.Sum(l => l.Quantity), wantedOnReservedLots = 0m;
                // stock of a lot another project holds: this document takes only what that lot has beyond the reservation
                foreach (var kv in lotBound)
                {
                    decimal q = mine.Where(l => l.LotId == kv.Key).Sum(l => l.Quantity);
                    wantedOnReservedLots += q;
                    decimal lotOnHand = stock.Where(x => x.ItemId == itemId && x.LotId == kv.Key).Sum(x => x.Quantity);
                    if (q > 0 && lotOnHand - kv.Value < q) return Refuse("بچ این حواله برای آن پروژه رزرو شده است؛ موجودی بچ " + BalanceRules.Num(lotOnHand) + "، رزرو " + BalanceRules.Num(kv.Value) + " و مقدار این حواله " + BalanceRules.Num(q));
                }
                decimal wantedFree = wantedTotal - wantedOnReservedLots;
                decimal freeUnbound = onHand - lotBound.Values.Sum() - unbound;
                if (unbound > 0 && wantedFree > 0 && freeUnbound < wantedFree)
                    return Refuse("موجودی " + BalanceRules.Num(onHand) + "، رزرو پروژه‌های دیگر " + BalanceRules.Num(unbound + lotBound.Values.Sum()) + " و مقدار این حواله " + BalanceRules.Num(wantedTotal));
            }
            return null;
        }

        /// <summary>
        /// A confirmed issue (or a send to a contractor) to a work order uses up the reservations of that work order, earliest need first (the stock leaves the reservation and goes to the project). A reservation of one lot or one serial unit
        /// is used only by that lot or unit; a reservation of an item without lot is used by any lot of it.
        /// </summary>
        public static async Task ApplyIssueAsync(IUnitOfWork uow, InventoryDocument document, int userId, CancellationToken ct)
        {
            int? workOrderId = await ReservationWorkOrderAsync(uow, document, ct);
            if (!workOrderId.HasValue) return;
            List<int> ids = document.Lines.Select(l => l.ItemId).Distinct().ToList();
            List<ProjectReservation> mine = await uow.Set<ProjectReservation>().Where(r => !r.Deleted && r.Status == ReservationStatus.Active && r.WorkOrderId == workOrderId.Value && ids.Contains(r.ItemId)).ToListAsync(ct);
            if (mine.Count == 0) return;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (InventoryDocumentLine line in document.Lines.OrderBy(l => l.RowNumber))
            {
                decimal left = line.Quantity;
                var unitIds = new HashSet<int>((line.Serials ?? new List<InventoryDocumentLineSerial>()).Where(x => x.SerialUnitId.HasValue).Select(x => x.SerialUnitId.Value));
                IEnumerable<ProjectReservation> candidates = mine.Where(x => x.ItemId == line.ItemId && (x.LotId == null || x.LotId == line.LotId) && (x.SerialUnitId == null || unitIds.Contains(x.SerialUnitId.Value)))
                    .OrderBy(x => x.LotId == line.LotId && x.LotId.HasValue ? 0 : 1).ThenBy(x => x.NeedDate ?? DateTime.MaxValue).ThenBy(x => x.Id);
                foreach (ProjectReservation r in candidates)
                {
                    if (left <= 0) break;
                    decimal take = Math.Min(left, Math.Max(0m, r.Quantity - r.IssuedQuantity));
                    if (take <= 0) continue;
                    r.IssuedQuantity += take; left -= take;
                    if (r.IssuedQuantity >= r.Quantity) r.Status = ReservationStatus.FullyIssued;
                    r.ModifyDate = now; r.ModifierUserId = userId;
                }
            }
        }

        /// <summary>The issue is cancelled: what it used up of the reservations of its work order goes back to them (the reservation is active again when it had been fully issued).</summary>
        public static async Task UndoIssueAsync(IUnitOfWork uow, InventoryDocument document, int userId, CancellationToken ct)
        {
            int? workOrderId = await ReservationWorkOrderAsync(uow, document, ct);
            if (!workOrderId.HasValue) return;
            List<InventoryDocumentLine> lines = await uow.Set<InventoryDocumentLine>().Include(l => l.Serials).Where(l => l.DocumentId == document.Id).ToListAsync(ct);
            List<int> ids = lines.Select(l => l.ItemId).Distinct().ToList();
            List<ProjectReservation> mine = await uow.Set<ProjectReservation>().Where(r => !r.Deleted && (r.Status == ReservationStatus.Active || r.Status == ReservationStatus.FullyIssued) && r.WorkOrderId == workOrderId.Value && ids.Contains(r.ItemId) && r.IssuedQuantity > 0).ToListAsync(ct);
            if (mine.Count == 0) return;
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (InventoryDocumentLine line in lines.OrderByDescending(l => l.RowNumber))
            {
                decimal left = line.Quantity;
                var unitIds = new HashSet<int>((line.Serials ?? new List<InventoryDocumentLineSerial>()).Where(x => x.SerialUnitId.HasValue).Select(x => x.SerialUnitId.Value));
                // the opposite order of the issue: the reservation used last gives back first
                IEnumerable<ProjectReservation> candidates = mine.Where(x => x.ItemId == line.ItemId && (x.LotId == null || x.LotId == line.LotId) && (x.SerialUnitId == null || unitIds.Contains(x.SerialUnitId.Value)))
                    .OrderBy(x => x.LotId == line.LotId && x.LotId.HasValue ? 1 : 0).ThenByDescending(x => x.NeedDate ?? DateTime.MaxValue).ThenByDescending(x => x.Id);
                foreach (ProjectReservation r in candidates)
                {
                    if (left <= 0) break;
                    decimal back = Math.Min(left, r.IssuedQuantity);
                    if (back <= 0) continue;
                    r.IssuedQuantity -= back; left -= back;
                    if (r.Status == ReservationStatus.FullyIssued && r.IssuedQuantity < r.Quantity) r.Status = ReservationStatus.Active;
                    r.ModifyDate = now; r.ModifierUserId = userId;
                }
            }
        }
    }
}
