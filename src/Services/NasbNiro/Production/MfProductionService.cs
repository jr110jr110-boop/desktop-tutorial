using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.Pagination;
using QcRecordDto = ViewModels.NasbNiro.QualityDtos.QcRecordDto;
using SaveQcRecordDto = ViewModels.NasbNiro.QualityDtos.SaveQcRecordDto;
using SaveQcMeasurementDto = ViewModels.NasbNiro.QualityDtos.SaveQcMeasurementDto;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    public interface IMfProductionService
    {
        Task<ResponseDto<PagedList<OrderListDto>>> GetOrdersAsync(OrderQuery query, CancellationToken ct);
        Task<ResponseDto<OrderDto>> GetOrderAsync(int id, CancellationToken ct);
        Task<ResponseDto<OrderDto>> SaveComponentsAsync(SaveComponentsDto dto, CancellationToken ct);
        Task<ResponseDto<ReleaseCheckResultDto>> ReleaseCheckAsync(int id, CancellationToken ct);
        Task<ResponseDto<OrderDto>> ReleaseAsync(ReleaseOrderDto dto, CancellationToken ct);
        Task<ResponseDto<OrderDto>> OrderActionAsync(OrderActionDto dto, CancellationToken ct);

        Task<ResponseDto<PagedList<OperationDto>>> GetOperationsAsync(OperationQuery query, CancellationToken ct);
        Task<ResponseDto<OperationDto>> OperationActionAsync(OperationActionDto dto, CancellationToken ct);
        Task<ResponseDto<OperationDto>> QcDecisionAsync(QcDecisionDto dto, CancellationToken ct);
        Task<ResponseDto<OperationDto>> ReworkAsync(ReworkOperationDto dto, CancellationToken ct);
    }

    /// <summary>Production orders: the release gate, the kit, readiness of the operations, QC hold, rework, outside work and completion.</summary>
    public class MfProductionService : IMfProductionService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IInspectionService _inspections;
        private readonly IInventoryDocumentService _documents;
        private readonly MfFlow _flow;

        public MfProductionService(IUnitOfWork uow, IInventoryUserContext user, IInventoryDocumentService documents, IInspectionService inspections, INotificationService notify = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _inspections = inspections ?? throw new ArgumentNullException(nameof(inspections));
            _notify = notify;
            _documents = documents;
            _flow = new MfFlow(uow, user, documents, notify);
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => MfText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => MfText.Bad<T>(m);

        private static PagedList<T> AsPage<T>(PagedList<T> all, int total, int number, int size)
        {
            all.TotalCount = total;
            all.CurrentPage = number;
            all.PageSize = size;
            all.TotalPages = (int)Math.Ceiling(total / (double)Math.Max(size, 1));
            return all;
        }

        // ================================================================================== mapping

        private static string JobCard(string orderNumber, MfOperation op) => MfMapping.JobCard(orderNumber, op);

        private Task<List<OperationDto>> OperationDtosAsync(List<MfOperation> ops, CancellationToken ct) => MfMapping.OperationDtosAsync(_uow, ops, ct);

        private async Task<List<OrderListDto>> OrderListDtosAsync(List<MfProductionOrder> orders, CancellationToken ct)
        {
            if (orders.Count == 0) return new List<OrderListDto>();
            List<int> ids = orders.Select(o => o.Id).ToList();
            List<int> woIds = orders.Select(o => o.WorkOrderId).Distinct().ToList();
            Dictionary<int, string> workOrders = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            List<int> itemIds = orders.Where(o => o.ItemId.HasValue).Select(o => o.ItemId.Value).Distinct().ToList();
            var items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title }).ToListAsync(ct);
            var counts = await _uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && ids.Contains(o.OrderId))
                .GroupBy(o => o.OrderId).Select(g => new { Id = g.Key, Total = g.Count(), Done = g.Count(o => o.Status == MfOperationStatus.Done || o.Status == MfOperationStatus.Skipped) }).ToListAsync(ct);
            Dictionary<int, MfFlow.Urgency> urgency = await MfFlow.UrgencyAsync(_uow, ct);
            return orders.Select(o =>
            {
                var item = items.FirstOrDefault(i => i.Id == o.ItemId);
                var c = counts.FirstOrDefault(x => x.Id == o.Id);
                urgency.TryGetValue(o.Id, out MfFlow.Urgency u);
                return new OrderListDto
                {
                    Id = o.Id, Number = o.Number, WorkOrderId = o.WorkOrderId, WorkOrderCode = workOrders.TryGetValue(o.WorkOrderId, out string code) ? code : null, ContractId = o.ContractId,
                    ProcessId = o.ProcessId, ItemId = o.ItemId, ItemCode = item?.Code, ItemTitle = item?.Title, Quantity = o.Quantity, Unit = o.Unit, Status = o.Status,
                    WrittenOffQty = o.WrittenOffQty, EffectiveQuantity = MfQuantityRules.EffectiveOrderQuantity(o.Quantity, o.WrittenOffQty), ReplacesOrderId = o.ReplacesOrderId,
                    RoutingRevisionId = o.RoutingRevisionId, Priority = u != null && u.Priority > 0 ? u.Priority : (int?)null, TotalFloatDays = u?.TotalFloat, NeedDate = u?.NeedDate,
                    NeedDateText = MfText.Date(u?.NeedDate), PlannedStart = o.PlannedStart, PlannedStartText = MfText.Date(o.PlannedStart), PlannedFinish = o.PlannedFinish,
                    PlannedFinishText = MfText.Date(o.PlannedFinish), ActualStartText = MfText.Date(o.ActualStart), ActualFinishText = MfText.Date(o.ActualFinish),
                    OperationCount = c?.Total ?? 0, OperationsDone = c?.Done ?? 0, AutoDrafted = o.AutoDrafted,
                    IsLate = o.IsLate, DaysLate = o.DaysLate, KitReadyDateText = MfText.Date(o.KitReadyDate), KitShortage = o.KitShortage, ScheduleNote = o.ScheduleNote, ScheduledAtText = MfText.Stamp(o.ScheduledAt),
                };
            }).ToList();
        }

        private async Task<OrderDto> OrderDtoAsync(MfProductionOrder o, bool withRelease, CancellationToken ct)
        {
            OrderListDto head = (await OrderListDtosAsync(new List<MfProductionOrder> { o }, ct))[0];
            var dto = new OrderDto
            {
                Id = head.Id, Number = head.Number, WorkOrderId = head.WorkOrderId, WorkOrderCode = head.WorkOrderCode, ContractId = head.ContractId, ProcessId = head.ProcessId,
                ItemId = head.ItemId, ItemCode = head.ItemCode, ItemTitle = head.ItemTitle, Quantity = head.Quantity, Unit = head.Unit, Status = head.Status,
                WrittenOffQty = head.WrittenOffQty, EffectiveQuantity = head.EffectiveQuantity, ReplacesOrderId = head.ReplacesOrderId,
                RoutingRevisionId = head.RoutingRevisionId, Priority = head.Priority, TotalFloatDays = head.TotalFloatDays, NeedDate = head.NeedDate, NeedDateText = head.NeedDateText,
                PlannedStart = head.PlannedStart, PlannedStartText = head.PlannedStartText, PlannedFinish = head.PlannedFinish, PlannedFinishText = head.PlannedFinishText,
                ActualStartText = head.ActualStartText, ActualFinishText = head.ActualFinishText, OperationCount = head.OperationCount, OperationsDone = head.OperationsDone,
                AutoDrafted = head.AutoDrafted, IsLate = head.IsLate, DaysLate = head.DaysLate, KitReadyDateText = head.KitReadyDateText, KitShortage = head.KitShortage,
                ScheduleNote = head.ScheduleNote, ScheduledAtText = head.ScheduledAtText, BomId = o.BomId, ActivityId = o.ActivityId, SerialsPlanned = o.SerialsPlanned, KitRequestId = o.KitRequestId,
                KitOverrideReason = o.KitOverrideReason, ReleaseOverride = o.ReleaseOverride, ReleaseOverrideReason = o.ReleaseOverrideReason, ShortageText = o.ShortageText,
                ReleasedAtText = MfText.Stamp(o.ReleasedAt), ProductionReceiptId = o.ProductionReceiptId, CancelReason = o.CancelReason, Note = o.Note, RowVersion = MfConcurrency.Text(o.RowVersion),
            };
            if (o.ReplacesOrderId.HasValue)
                dto.ReplacesOrderNumber = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(x => x.Id == o.ReplacesOrderId.Value).Select(x => x.Number).FirstOrDefaultAsync(ct);
            dto.ReplacementOrderNumber = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(x => !x.Deleted && x.ReplacesOrderId == o.Id).OrderBy(x => x.Id).Select(x => x.Number).FirstOrDefaultAsync(ct);
            if (o.ReleasedByUserId.HasValue) dto.ReleasedByName = (await InventoryUsers.GetNamesAsync(_uow, new int?[] { o.ReleasedByUserId }, ct)).Name(o.ReleasedByUserId);
            if (o.KitRequestId.HasValue)
            {
                dto.KitNumber = await _uow.Set<MaterialRequest>().AsNoTracking().Where(m => m.Id == o.KitRequestId.Value).Select(m => m.Number).FirstOrDefaultAsync(ct);
                dto.KitIssued = o.Status != MfOrderStatus.Draft && await _flow.KitReadyAsync(new MfProductionOrder { KitRequestId = o.KitRequestId }, ct);
            }
            if (o.ProductionReceiptId.HasValue)
                dto.ProductionReceiptNumber = await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.Id == o.ProductionReceiptId.Value).Select(d => d.Number).FirstOrDefaultAsync(ct);
            List<MfOrderComponent> comps = await _uow.Set<MfOrderComponent>().AsNoTracking().Where(c => c.OrderId == o.Id).OrderBy(c => c.Id).ToListAsync(ct);
            List<int> cItemIds = comps.Select(c => c.ItemId).ToList();
            var cItems = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => cItemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title }).ToListAsync(ct);
            dto.Components = comps.Select(c =>
            {
                var i = cItems.FirstOrDefault(x => x.Id == c.ItemId);
                return new OrderComponentDto { ItemId = c.ItemId, ItemCode = i?.Code, ItemTitle = i?.Title, Quantity = c.Quantity, Unit = c.Unit };
            }).ToList();
            List<MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(x => x.OrderId == o.Id && !x.Deleted).ToListAsync(ct);
            dto.Operations = await OperationDtosAsync(MfFlow.Chain(ops), ct);
            if (withRelease && o.Status == MfOrderStatus.Draft) dto.Release = await ComputeChecksAsync(o, ops, comps, ct);
            return dto;
        }

        // ================================================================================== queries

        public async Task<ResponseDto<PagedList<OrderListDto>>> GetOrdersAsync(OrderQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<PagedList<OrderListDto>>(MfText.NotAllowed);
            query ??= new OrderQuery();
            IQueryable<MfProductionOrder> q = _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted);
            if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);
            if (query.WorkOrderId.HasValue) q = q.Where(o => o.WorkOrderId == query.WorkOrderId.Value);
            if (query.ContractId.HasValue) q = q.Where(o => o.ContractId == query.ContractId.Value);
            if (query.ItemId.HasValue) q = q.Where(o => o.ItemId == query.ItemId.Value);
            if (query.OpenOnly == true) q = q.Where(o => o.Status == MfOrderStatus.Draft || o.Status == MfOrderStatus.Released || o.Status == MfOrderStatus.InProgress);
            if (query.LateOnly == true) q = q.Where(o => o.IsLate);
            string text = Clean(query.Contains);
            if (text != null)
                q = q.Where(o => o.Number.Contains(text) || _uow.Set<InventoryItem>().Any(i => i.Id == o.ItemId && (i.Code.Contains(text) || i.Title.Contains(text)))
                    || _uow.Set<WorkOrder>().Any(w => w.Id == o.WorkOrderId && w.Code.Contains(text)));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfProductionOrder> rows = await q.OrderByDescending(o => o.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(await OrderListDtosAsync(rows, ct), 1, int.MaxValue, ct), total, number, size));
        }

        public async Task<ResponseDto<OrderDto>> GetOrderAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<OrderDto>(MfText.NotAllowed);
            MfProductionOrder o = await _uow.Set<MfProductionOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (o == null) return MfText.NotFound<OrderDto>("دستور تولید پیدا نشد.");
            return ResponseDtoHelper.Ok(await OrderDtoAsync(o, true, ct));
        }

        public async Task<ResponseDto<PagedList<OperationDto>>> GetOperationsAsync(OperationQuery query, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<PagedList<OperationDto>>(MfText.NotAllowed);
            query ??= new OperationQuery();
            IQueryable<MfOperation> q = _uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted);
            if (query.Id.HasValue) q = q.Where(o => o.Id == query.Id.Value);
            if (query.OrderId.HasValue) q = q.Where(o => o.OrderId == query.OrderId.Value);
            if (query.WorkCenterId.HasValue) q = q.Where(o => o.WorkCenterId == query.WorkCenterId.Value);
            if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(o => o.Title.Contains(text) || _uow.Set<MfProductionOrder>().Any(p => p.Id == o.OrderId && p.Number.Contains(text)));
            int total = await q.CountAsync(ct);
            int size = MfText.Size(query.PageSize);
            int number = query.PageNumber <= 0 ? 1 : query.PageNumber;
            List<MfOperation> rows = await q.OrderByDescending(o => o.OrderId).ThenBy(o => o.Sequence).ThenBy(o => o.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return ResponseDtoHelper.Ok(AsPage(await MfText.PageAsync(await OperationDtosAsync(rows, ct), 1, int.MaxValue, ct), total, number, size));
        }

        // ================================================================================== components

        public async Task<ResponseDto<OrderDto>> SaveComponentsAsync(SaveComponentsDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Planners)) return Bad<OrderDto>(MfText.NotAllowed);
            if (dto == null || dto.Components == null) return Bad<OrderDto>(MfText.InvalidData);
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().FirstOrDefaultAsync(o => o.Id == dto.OrderId && !o.Deleted, ct);
            if (order == null) return MfText.NotFound<OrderDto>("دستور تولید پیدا نشد.");
            if (order.Status != MfOrderStatus.Draft) return Bad<OrderDto>("اقلام مصرفی فقط تا پیش از صدور دستور قابل ویرایش است.");
            if (!MfConcurrency.Apply(_uow, order, dto.RowVersion)) return Bad<OrderDto>(MfText.InvalidData);
            if (dto.Components.Any(c => c.Quantity <= 0)) return Bad<OrderDto>("مقدار هر قلم باید بیشتر از صفر باشد.");
            List<int> itemIds = dto.Components.Select(c => c.ItemId).Distinct().ToList();
            Dictionary<int, InventoryItem> items = await _uow.Set<InventoryItem>().Where(i => itemIds.Contains(i.Id) && !i.Deleted && i.IsActive).ToDictionaryAsync(i => i.Id, ct);
            if (items.Count != itemIds.Count) return Bad<OrderDto>("کالای یکی از اقلام پیدا نشد یا غیرفعال است.");
            if (order.ItemId.HasValue && itemIds.Contains(order.ItemId.Value)) return Bad<OrderDto>("کالای خروجی نمی‌تواند خودش قلم مصرفی باشد.");

            List<MfOrderComponent> existing = await _uow.Set<MfOrderComponent>().Where(c => c.OrderId == order.Id).ToListAsync(ct);
            _uow.Set<MfOrderComponent>().RemoveRange(existing);
            foreach (var g in dto.Components.GroupBy(c => c.ItemId))
                order.Components.Add(new MfOrderComponent { ItemId = g.Key, Quantity = g.Sum(c => c.Quantity), Unit = items[g.Key].Unit });
            order.SerialsPlanned = Clean(dto.SerialsPlanned);
            order.EditedByUserId = Me;
            order.ModifyDate = DateTimeOffset.Now;
            order.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<OrderDto>(MfText.Concurrency);
            }
            return ResponseDtoHelper.Ok(await OrderDtoAsync(order, true, ct));
        }

        // ================================================================================== release checks

        private static string Join(IEnumerable<string> parts) => string.Join("؛ ", parts);

        private async Task<ReleaseCheckResultDto> ComputeChecksAsync(MfProductionOrder order, List<MfOperation> ops, List<MfOrderComponent> comps, CancellationToken ct)
        {
            var result = new ReleaseCheckResultDto { OrderId = order.Id };
            List<MfOperation> chain = MfFlow.Chain(ops);

            // 1. routing: an approved revision whose approved content is the content of the steps now
            string routingMessage;
            bool routingOk = false;
            InventoryItem item = order.ItemId.HasValue ? await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.ItemId.Value, ct) : null;
            if (item == null) routingMessage = "کالای دستور تولید مشخص نیست.";
            else if (chain.Count == 0) routingMessage = "دستور هنوز عملیاتی ندارد؛ پس از تأیید مسیر ساخت، «به‌روزرسانی مسیر» را بزنید.";
            else
            {
                MfResolvedRouting routing = await MfRouting.ResolveAsync(_uow, item, ct);
                routingOk = routing.Revision != null;
                routingMessage = routingOk ? $"مسیر ساخت بازنگری {routing.Revision.Revision} تأیید شده است." : routing.Message;
                if (routingOk && order.RoutingRevisionId != routing.Revision.Id)
                {
                    // the operations were copied from an older revision: the order must follow the current approved one before it is released
                    routingOk = false;
                    routingMessage = $"عملیات دستور از بازنگری قدیمی مسیر ساخت گرفته شده است و بازنگری {routing.Revision.Revision} اکنون جاری است؛ «به‌روزرسانی مسیر» (refresh-routing) را بزنید.";
                }
            }
            result.Checks.Add(new ReleaseCheckDto { Code = "routing", Title = "مسیر ساخت تأییدشده", Passed = routingOk, Message = routingMessage });

            // 2. work centres exist, are active and not retired
            var problems = new List<string>();
            foreach (MfOperation op in chain.Where(o => !o.IsOutside))
            {
                if (!op.WorkCenterId.HasValue) { problems.Add($"عملیات {op.Sequence} مرکز کار ندارد"); continue; }
                MfWorkCenter wc = await _uow.Set<MfWorkCenter>().AsNoTracking().FirstOrDefaultAsync(w => w.Id == op.WorkCenterId.Value && !w.Deleted, ct);
                if (wc == null || !wc.IsActive) { problems.Add($"مرکز کار عملیات {op.Sequence} وجود ندارد یا غیرفعال است"); continue; }
                int machines = await _uow.Set<MfMachineInfo>().CountAsync(i => i.WorkCenterId == wc.Id && !i.Deleted, ct);
                if (machines > 0 && !await _uow.Set<MfMachineInfo>().AnyAsync(i => i.WorkCenterId == wc.Id && !i.Deleted && i.Status != MfMachineStatus.Retired, ct))
                    problems.Add($"همه ماشین‌های مرکز کار {wc.Code} بازنشسته‌اند");
            }
            result.Checks.Add(new ReleaseCheckDto { Code = "workcenters", Title = "مراکز کار", Passed = problems.Count == 0 && chain.Count > 0, Message = problems.Count > 0 ? Join(problems.Distinct()) : chain.Count == 0 ? "عملیاتی وجود ندارد." : "مراکز کار معتبرند." });

            // 3. drawings the routing needs are approved MDR lines of this work order
            List<int> templateIds = chain.Where(o => o.OperationTemplateId.HasValue).Select(o => o.OperationTemplateId.Value).Distinct().ToList();
            List<string> rules = (await _uow.Set<MfOperationStandard>().AsNoTracking().Where(s => !s.Deleted && templateIds.Contains(s.OperationTemplateId) && s.DrawingReferenceRule != null)
                .Select(s => s.DrawingReferenceRule).ToListAsync(ct))
                .SelectMany(r => r.Split(new[] { ',', ';', '،', '؛' }, StringSplitOptions.RemoveEmptyEntries)).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct().ToList();
            var missing = new List<string>();
            foreach (string rule in rules)
            {
                string word = rule;
                bool found = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().AnyAsync(d => !d.Deleted && d.WorkOrderId == order.WorkOrderId && d.IssueState == PcIssueState.Approved
                    && ((d.DocumentType != null && d.DocumentType.Contains(word)) || (d.Title != null && d.Title.Contains(word))), ct);
                if (!found) missing.Add(word);
            }
            result.Checks.Add(new ReleaseCheckDto
            {
                Code = "drawings", Title = "نقشه‌های تأییدشده", Passed = missing.Count == 0,
                Message = rules.Count == 0 ? "مسیر ساخت نقشه‌ای لازم ندارد." : missing.Count == 0 ? "نقشه‌های لازم در MDR تأیید شده‌اند." : "نقشه تأییدشده در MDR دستور کار پیدا نشد برای: " + Join(missing),
            });

            // 4. the work order has an ITP and it is approved (R1p D19: no ITP at all is a refusal, not a pass; the ITP is drafted from the quality template when the order is drafted)
            Itp itp = await _uow.Set<Itp>().AsNoTracking().Where(i => i.WorkOrderId == order.WorkOrderId && !i.Deleted).OrderByDescending(i => i.Revision).ThenByDescending(i => i.Id).FirstOrDefaultAsync(ct);
            string workOrderCode = itp != null ? null : await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == order.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            (bool itpOk, string itpMessage) = MfReleaseRules.Itp(itp != null, itp?.Status ?? ItpStatus.Draft, itp?.Number, workOrderCode);
            result.Checks.Add(new ReleaseCheckDto { Code = "itp", Title = "برنامه بازرسی و آزمون (ITP)", Passed = itpOk, Message = itpMessage });

            // 5. materials: on hand (not reserved) plus open purchase order lines that arrive in time
            result.Shortages = await ShortagesAsync(order, comps, ct);
            result.Checks.Add(new ReleaseCheckDto
            {
                Code = "materials", Title = "موجودی مواد", Passed = result.Shortages.Count == 0, Overridable = true,
                Message = result.Shortages.Count == 0 ? "موجودی اقلام مصرفی کافی است." : "کمبود: " + Join(result.Shortages.Select(s => $"{s.ItemCode} {MfFlow.Num(s.Short)}")),
            });

            result.CanRelease = result.Checks.All(c => c.Passed);
            result.CanReleaseWithOverride = result.Checks.Where(c => !c.Overridable).All(c => c.Passed);
            return result;
        }

        private async Task<List<ShortageDto>> ShortagesAsync(MfProductionOrder order, List<MfOrderComponent> comps, CancellationToken ct)
        {
            var list = new List<ShortageDto>();
            if (comps.Count == 0) return list;
            List<int> itemIds = comps.Select(c => c.ItemId).Distinct().ToList();
            var items = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Title }).ToListAsync(ct);
            WarehouseKind[] company = { WarehouseKind.General, WarehouseKind.RawMaterial, WarehouseKind.Parts, WarehouseKind.WorkInProgress, WarehouseKind.FinishedGoods };
            var onHand = await (from b in _uow.Set<StockBalance>().AsNoTracking()
                                join w in _uow.Set<Warehouse>().AsNoTracking() on b.WarehouseId equals w.Id
                                where itemIds.Contains(b.ItemId) && !w.Deleted && w.IsActive && company.Contains(w.Kind)
                                group b by b.ItemId into g
                                select new { Id = g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct);

            // reserved: what open material requests still have to issue (the drafts of other released orders' kits count too)
            List<int> kitIds = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && o.KitRequestId != null && o.Id != order.Id
                && (o.Status == MfOrderStatus.Released || o.Status == MfOrderStatus.InProgress)).Select(o => o.KitRequestId.Value).ToListAsync(ct);
            int ownKit = order.KitRequestId ?? 0;
            var reservedLines = await (from l in _uow.Set<MaterialRequestLine>().AsNoTracking()
                                       join r in _uow.Set<MaterialRequest>().AsNoTracking() on l.MaterialRequestId equals r.Id
                                       where itemIds.Contains(l.ItemId) && !r.Deleted && r.Id != ownKit
                                             && (r.Status == MaterialRequestStatus.Submitted || r.Status == MaterialRequestStatus.Approved || (r.Status == MaterialRequestStatus.Draft && kitIds.Contains(r.Id)))
                                       select new { l.ItemId, l.Quantity, l.IssuedQuantity }).ToListAsync(ct);

            // stock held for confirmed customer sales orders is not available to the plant
            Dictionary<int, decimal> salesHeld = await Sales.SlReservations.ReservedAsync(_uow, itemIds, ct);

            DateTime horizon = (order.PlannedStart ?? DateTime.Today).Date;
            PurchaseOrderStatus[] open = { PurchaseOrderStatus.Approved, PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived };
            var incomingLines = await (from l in _uow.Set<PurchaseOrderLine>().AsNoTracking()
                                       join p in _uow.Set<PurchaseOrder>().AsNoTracking() on l.PurchaseOrderId equals p.Id
                                       where l.ItemId != null && itemIds.Contains(l.ItemId.Value) && !p.Deleted && open.Contains(p.Status)
                                       select new { ItemId = l.ItemId.Value, l.Quantity, l.ReceivedQuantity, Delivery = l.DeliveryDate ?? p.DeliveryDate }).ToListAsync(ct);

            foreach (var g in comps.GroupBy(c => c.ItemId))
            {
                decimal required = g.Sum(c => c.Quantity);
                decimal stock = onHand.FirstOrDefault(x => x.Id == g.Key)?.Qty ?? 0;
                decimal reserved = reservedLines.Where(l => l.ItemId == g.Key).Sum(l => Math.Max(l.Quantity - l.IssuedQuantity, 0)) + salesHeld.GetValueOrDefault(g.Key);
                decimal incoming = incomingLines.Where(l => l.ItemId == g.Key && l.Delivery.HasValue && l.Delivery.Value.Date <= horizon).Sum(l => Math.Max(l.Quantity - l.ReceivedQuantity, 0));
                decimal available = stock - reserved + incoming;
                decimal shortQty = required - available;
                if (shortQty <= 0) continue;
                var item = items.FirstOrDefault(i => i.Id == g.Key);
                list.Add(new ShortageDto { ItemId = g.Key, ItemCode = item?.Code, ItemTitle = item?.Title, Required = required, OnHand = stock, Reserved = reserved, Incoming = incoming, Available = available, Short = shortQty });
            }
            return list;
        }

        public async Task<ResponseDto<ReleaseCheckResultDto>> ReleaseCheckAsync(int id, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.MasterReaders)) return Bad<ReleaseCheckResultDto>(MfText.NotAllowed);
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && !o.Deleted, ct);
            if (order == null) return MfText.NotFound<ReleaseCheckResultDto>("دستور تولید پیدا نشد.");
            List<MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => o.OrderId == id && !o.Deleted).ToListAsync(ct);
            List<MfOrderComponent> comps = await _uow.Set<MfOrderComponent>().AsNoTracking().Where(c => c.OrderId == id).ToListAsync(ct);
            return ResponseDtoHelper.Ok(await ComputeChecksAsync(order, ops, comps, ct));
        }

        // ================================================================================== release

        public async Task<ResponseDto<OrderDto>> ReleaseAsync(ReleaseOrderDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<OrderDto>("صدور دستور تولید فقط با سرپرست کارگاه یا مدیر تولید است.");
            if (dto == null) return Bad<OrderDto>(MfText.InvalidData);
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().FirstOrDefaultAsync(o => o.Id == dto.Id && !o.Deleted, ct);
            if (order == null) return MfText.NotFound<OrderDto>("دستور تولید پیدا نشد.");
            if (order.Status != MfOrderStatus.Draft) return Bad<OrderDto>("فقط دستور تولید پیش‌نویس صادر می‌شود.");
            if (!MfConcurrency.Apply(_uow, order, dto.RowVersion)) return Bad<OrderDto>(MfText.InvalidData);
            if (order.EditedByUserId == Me || (!order.AutoDrafted && order.CreatorUserId == Me))
                return Bad<OrderDto>(MfText.SameUser + " (کسی که دستور را ساخته یا اقلام آن را ویرایش کرده نمی‌تواند آن را صادر کند)");

            List<MfOperation> ops = await _uow.Set<MfOperation>().Where(o => o.OrderId == order.Id && !o.Deleted).ToListAsync(ct);
            List<MfOrderComponent> comps = await _uow.Set<MfOrderComponent>().AsNoTracking().Where(c => c.OrderId == order.Id).ToListAsync(ct);
            ReleaseCheckResultDto checks = await ComputeChecksAsync(order, ops, comps, ct);
            List<ReleaseCheckDto> hard = checks.Checks.Where(c => !c.Passed && !c.Overridable).ToList();
            if (hard.Count > 0) return Bad<OrderDto>("صدور ممکن نیست: " + Join(hard.Select(c => $"{c.Title} ({c.Message})")));
            bool shortage = checks.Shortages.Count > 0;
            string reason = Clean(dto.OverrideReason);
            if (shortage)
            {
                if (!_user.IsInRole(ProductionRoles.Manager)) return Bad<OrderDto>("کمبود موجودی وجود دارد؛ صدور با کمبود فقط با مدیر تولید و ذکر دلیل ممکن است. " + checks.Checks.First(c => c.Code == "materials").Message);
                if (reason == null) return Bad<OrderDto>("برای صدور با کمبود موجودی، دلیل را بنویسید. " + checks.Checks.First(c => c.Code == "materials").Message);
            }

            DateTimeOffset now = DateTimeOffset.Now;
            order.Status = MfOrderStatus.Released;
            order.ReleasedAt = now;
            order.ReleasedByUserId = Me;
            order.ReleaseOverride = shortage;
            order.ReleaseOverrideReason = shortage ? reason : null;
            order.ShortageText = shortage ? Join(checks.Shortages.Select(s => $"{s.ItemCode}: نیاز {MfFlow.Num(s.Required)}، موجودی قابل استفاده {MfFlow.Num(s.Available)}، کمبود {MfFlow.Num(s.Short)}")) : null;
            order.ModifyDate = now;
            order.ModifierUserId = Me;

            // the kit: a draft material request for the components (the existing approvals apply)
            if (comps.Count > 0)
            {
                var kit = new MaterialRequest
                {
                    Number = await InventoryNumbers.NextNumberAsync(_uow, InventoryNumbers.MaterialRequest, "MR", DateTime.Today, ct),
                    RequestDate = DateTime.Today, Status = MaterialRequestStatus.Draft, WorkOrderId = order.WorkOrderId, Purpose = $"کیت ساخت دستور تولید {order.Number}",
                    CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me, Lines = new List<MaterialRequestLine>(),
                };
                int row = 1;
                foreach (MfOrderComponent c in comps) kit.Lines.Add(new MaterialRequestLine { RowNumber = row++, ItemId = c.ItemId, Quantity = c.Quantity, Description = $"دستور تولید {order.Number}" });
                _uow.Set<MaterialRequest>().Add(kit);
                order.KitRequest = kit;
            }

            await CreatePartOperationsAsync(order, ops, now, ct);
            List<string> notes = await _flow.ApplyAsync(order, ops, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<OrderDto>(MfText.Concurrency);
            }
            notes.AddRange(await _flow.AfterSaveAsync(ct));
            await MfGates.OrderAsync(_notify, order, ct);
            ResponseDto<OrderDto> ok = ResponseDtoHelper.Ok(await OrderDtoAsync(order, false, ct), notes.Count == 0 ? null : Join(notes));
            return ok;
        }

        /// <summary>PartOperation rows so the traceability screens keep working: per planned serial (the units that already exist) or, for a lot-tracked item, per lot (the lot of the order).</summary>
        private async Task CreatePartOperationsAsync(MfProductionOrder order, List<MfOperation> ops, DateTimeOffset now, CancellationToken ct)
        {
            if (!order.ItemId.HasValue) return;
            List<MfOperation> chain = MfFlow.Chain(ops).Where(o => !o.IsRework).ToList();
            InventoryItem item = await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.ItemId.Value, ct);
            if (item == null) return;

            if (item.TrackingMode == ItemTrackingMode.Lot)
            {
                // the lot number of the production receipt is the order number: the lot is made here (or found) so the receipt joins the same lot
                InventoryLot lot = await _uow.Set<InventoryLot>().FirstOrDefaultAsync(l => !l.Deleted && l.ItemId == item.Id && l.LotNumber == order.Number, ct);
                if (lot == null)
                {
                    lot = new InventoryLot { ItemId = item.Id, LotNumber = order.Number, Description = $"بچ دستور تولید {order.Number}", CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me };
                    _uow.Set<InventoryLot>().Add(lot);
                }
                List<int?> doneTemplates = lot.Id == 0 ? new List<int?>()
                    : await _uow.Set<PartOperation>().AsNoTracking().Where(p => !p.Deleted && p.LotId == lot.Id).Select(p => p.OperationTemplateId).ToListAsync(ct);
                foreach (MfOperation op in chain)
                {
                    if (op.OperationTemplateId.HasValue && doneTemplates.Contains(op.OperationTemplateId)) continue;
                    PartOperation part = NewPart(order, op, now);
                    part.Lot = lot;
                    _uow.Set<MfOperationPart>().Add(new MfOperationPart { OperationId = op.Id, PartOperation = part });
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(order.SerialsPlanned)) return;
            List<string> serials = order.SerialsPlanned.Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
            if (serials.Count == 0) return;
            List<SerialUnit> units = await _uow.Set<SerialUnit>().Where(u => !u.Deleted && u.ItemId == order.ItemId.Value && serials.Contains(u.SerialNumber)).ToListAsync(ct);
            if (units.Count == 0) return;
            List<int> unitIds = units.Select(u => u.Id).ToList();
            var existing = await _uow.Set<PartOperation>().AsNoTracking().Where(p => !p.Deleted && p.SerialUnitId != null && unitIds.Contains(p.SerialUnitId.Value))
                .Select(p => new { UnitId = p.SerialUnitId.Value, p.OperationTemplateId }).ToListAsync(ct);
            foreach (SerialUnit unit in units)
            {
                foreach (MfOperation op in chain)
                {
                    if (op.OperationTemplateId.HasValue && existing.Any(e => e.UnitId == unit.Id && e.OperationTemplateId == op.OperationTemplateId)) continue;
                    PartOperation part = NewPart(order, op, now);
                    part.SerialUnitId = unit.Id;
                    _uow.Set<MfOperationPart>().Add(new MfOperationPart { OperationId = op.Id, PartOperation = part });
                }
            }
        }

        private PartOperation NewPart(MfProductionOrder order, MfOperation op, DateTimeOffset now) => new PartOperation
        {
            OperationTemplateId = op.OperationTemplateId, Sequence = op.Sequence, Title = op.Title, WorkOrderId = order.WorkOrderId,
            Status = PartOperationStatus.Pending, QcStatus = op.InspectionRequirement == MfInspectionRequirement.Qc || op.InspectionRequirement == MfInspectionRequirement.ItpPoint ? QcStatus.Pending : QcStatus.NotRequired,
            CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
        };

        // ================================================================================== order actions

        public async Task<ResponseDto<OrderDto>> OrderActionAsync(OrderActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<OrderDto>(MfText.InvalidData);
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().FirstOrDefaultAsync(o => o.Id == dto.Id && !o.Deleted, ct);
            if (order == null) return MfText.NotFound<OrderDto>("دستور تولید پیدا نشد.");
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            string reason = Clean(dto.Reason);
            DateTimeOffset now = DateTimeOffset.Now;
            List<MfOperation> ops = await _uow.Set<MfOperation>().Where(o => o.OrderId == order.Id && !o.Deleted).ToListAsync(ct);
            List<string> notes = new List<string>();

            switch (action)
            {
                case "cancel":
                    if (!_user.IsInRole(ProductionRoles.Planners)) return Bad<OrderDto>(MfText.NotAllowed);
                    if (reason == null) return Bad<OrderDto>("دلیل لغو را بنویسید.");
                    if (order.Status != MfOrderStatus.Draft && order.Status != MfOrderStatus.Released) return Bad<OrderDto>("فقط دستور پیش‌نویس یا صادرشده‌ای که کاری روی آن شروع نشده لغو می‌شود.");
                    if (order.Status == MfOrderStatus.Released && !_user.IsInRole(ProductionRoles.Supervisors)) return Bad<OrderDto>(MfText.NotAllowed);
                    if (ops.Any(o => o.StartedAt.HasValue)) return Bad<OrderDto>("روی این دستور کار شروع شده است و لغو نمی‌شود.");
                    order.Status = MfOrderStatus.Cancelled;
                    order.CancelReason = reason;
                    break;
                case "close":
                    if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<OrderDto>(MfText.NotAllowed);
                    if (order.Status != MfOrderStatus.Completed) return Bad<OrderDto>("فقط دستور تکمیل‌شده بسته می‌شود.");
                    order.Status = MfOrderStatus.Closed;
                    break;
                case "kit-override":
                    if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<OrderDto>(MfText.NotAllowed);
                    if (reason == null) return Bad<OrderDto>("دلیل شروع بدون تکمیل کیت را بنویسید.");
                    if (order.Status != MfOrderStatus.Released && order.Status != MfOrderStatus.InProgress) return Bad<OrderDto>("فقط دستور صادرشده یا در حال اجرا.");
                    order.KitOverrideReason = reason;
                    order.KitOverrideByUserId = Me;
                    notes = await _flow.ApplyAsync(order, ops, ct);
                    break;
                case "refresh-readiness":
                    if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<OrderDto>(MfText.NotAllowed);
                    notes = await _flow.ApplyAsync(order, ops, ct);
                    break;
                case "refresh-routing":
                    {
                        if (!_user.IsInRole(ProductionRoles.Planners)) return Bad<OrderDto>(MfText.NotAllowed);
                        if (order.Status != MfOrderStatus.Draft) return Bad<OrderDto>("مسیر فقط برای دستور پیش‌نویس به‌روزرسانی می‌شود.");
                        InventoryItem item = order.ItemId.HasValue ? await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.ItemId.Value, ct) : null;
                        if (item == null) return Bad<OrderDto>("کالای دستور تولید مشخص نیست.");
                        MfResolvedRouting routing = await MfRouting.ResolveAsync(_uow, item, ct);
                        if (routing.Revision == null) return Bad<OrderDto>(routing.Message);
                        _uow.Set<MfOperation>().RemoveRange(ops);
                        order.RoutingRevisionId = routing.Revision.Id;
                        foreach (MfOperation op in MfDraftingService.OperationsFromRouting(routing, order.Quantity, now, Me)) order.Operations.Add(op);
                        order.EditedByUserId = Me;
                        break;
                    }
                default:
                    return Bad<OrderDto>("عمل نامعتبر است.");
            }
            order.ModifyDate = now;
            order.ModifierUserId = Me;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<OrderDto>(MfText.Concurrency);
            }
            notes.AddRange(await _flow.AfterSaveAsync(ct));
            await MfGates.OrderAsync(_notify, order, ct);
            return ResponseDtoHelper.Ok(await OrderDtoAsync(order, true, ct), notes.Count == 0 ? null : Join(notes));
        }

        // ================================================================================== operation actions

        private async Task<(MfOperation Op, MfProductionOrder Order, List<MfOperation> All, string Error)> LoadOperationAsync(int id, CancellationToken ct)
        {
            MfOperation op = await _uow.Set<MfOperation>().FirstOrDefaultAsync(o => o.Id == id && !o.Deleted, ct);
            if (op == null) return (null, null, null, "عملیات پیدا نشد.");
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().FirstAsync(o => o.Id == op.OrderId, ct);
            List<MfOperation> all = await _uow.Set<MfOperation>().Where(o => o.OrderId == order.Id && !o.Deleted).ToListAsync(ct);
            return (op, order, all, null);
        }

        private async Task<OperationDto> OneAsync(MfOperation op, CancellationToken ct) => (await OperationDtosAsync(new List<MfOperation> { op }, ct))[0];

        public async Task<ResponseDto<OperationDto>> OperationActionAsync(OperationActionDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<OperationDto>(MfText.InvalidData);
            var loaded = await LoadOperationAsync(dto.Id, ct);
            if (loaded.Error != null) return MfText.NotFound<OperationDto>(loaded.Error);
            MfOperation op = loaded.Op;
            MfProductionOrder order = loaded.Order;
            string action = (dto.Action ?? string.Empty).Trim().ToLowerInvariant();
            string reason = Clean(dto.Reason);
            DateTimeOffset now = DateTimeOffset.Now;
            if (order.Status != MfOrderStatus.Released && order.Status != MfOrderStatus.InProgress) return Bad<OperationDto>("دستور تولید در وضعیتی نیست که عملیات آن تغییر کند.");
            List<string> notes;
            var actionNotes = new List<string>();
            var writeOffs = new List<MfWriteOff>();

            if (action == "skip")
            {
                if (!_user.IsInRole(ProductionRoles.Supervisor)) return Bad<OperationDto>("رد کردن یک مرحله فقط با سرپرست کارگاه است.");
                if (reason == null) return Bad<OperationDto>("دلیل رد کردن مرحله را بنویسید.");
                if (op.Status != MfOperationStatus.Waiting && op.Status != MfOperationStatus.Ready) return Bad<OperationDto>("فقط مرحله‌ای که کار روی آن شروع نشده رد می‌شود.");
                op.Status = MfOperationStatus.Skipped;
                op.SkipReason = reason;
            }
            else if (action == "complete-outside")
            {
                if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<OperationDto>(MfText.NotAllowed);
                if (!op.IsOutside) return Bad<OperationDto>("این عملیات بیرونی نیست.");
                if (op.Status != MfOperationStatus.Ready && op.Status != MfOperationStatus.InProgress) return Bad<OperationDto>("این عملیات بیرونی هنوز آماده نیست یا پایان یافته است.");
                if (!dto.DocumentId.HasValue) return Bad<OperationDto>("سند برگشت از پیمانکار را مشخص کنید.");
                InventoryDocument doc = await _uow.Set<InventoryDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == dto.DocumentId.Value && !d.Deleted, ct);
                if (doc == null || doc.Type != InventoryDocumentType.SubcontractReturn) return Bad<OperationDto>("سند برگشت از پیمانکار پیدا نشد.");
                if (doc.WorkOrderId != order.WorkOrderId || (op.OutsideSupplierId.HasValue && doc.ProviderId != op.OutsideSupplierId)) return Bad<OperationDto>("سند برگشت به این دستور کار یا پیمانکار مربوط نیست.");

                // (a) the return belongs to the issue to the contractor of THIS operation: same contractor and contractor warehouse, and it quotes the job number (or the number of the issue).
                // The job number is unique to the operation, so one return document can complete only one operation.
                InventoryDocument issue = op.SubcontractIssueId.HasValue ? await _uow.Set<InventoryDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == op.SubcontractIssueId.Value && !d.Deleted, ct) : null;
                if (issue == null) return Bad<OperationDto>("برای این عملیات حواله ارسال به پیمانکار ثبت نشده است؛ بدون آن برگشت از پیمانکار پذیرفته نمی‌شود.");
                string job = MfNames.Operation(order.Number, op.Sequence);
                string reference = Clean(doc.ReferenceNumber);
                bool linked = doc.ProviderId == issue.ProviderId && doc.WarehouseId == issue.DestinationWarehouseId
                    && reference != null && (string.Equals(reference, job, StringComparison.OrdinalIgnoreCase) || (issue.Number != null && string.Equals(reference, issue.Number, StringComparison.OrdinalIgnoreCase)));
                if (!linked) return Bad<OperationDto>($"سند برگشت مربوط به حواله ارسال این عملیات نیست؛ مرجع سند برگشت باید شماره برگه کار ({job}) یا شماره حواله ارسال باشد و پیمانکار و انبار پیمانکار با حواله ارسال یکی باشد.");
                if (issue.Status != InventoryDocumentStatus.Confirmed) return Bad<OperationDto>("حواله ارسال به پیمانکار این عملیات هنوز قطعی نشده است.");
                if (doc.Status != InventoryDocumentStatus.Confirmed) return Bad<OperationDto>("سند برگشت از پیمانکار هنوز قطعی نشده است.");

                // (c) what came back is accepted: the incoming inspection of the return document is done and accepted a quantity, or (without an inspection) a service acceptance of the order line exists
                List<QcInspection> inspections = await _uow.Set<QcInspection>().AsNoTracking().Where(q => !q.Deleted && q.ReceiptDocumentId == doc.Id).ToListAsync(ct);
                if (inspections.Any(q => q.Status == InspectionStatus.Pending)) return Bad<OperationDto>("بازرسی ورودی کالای برگشتی از پیمانکار هنوز انجام نشده است.");
                decimal accepted = inspections.Count > 0
                    ? inspections.Where(q => !order.ItemId.HasValue || q.ItemId == order.ItemId.Value).Sum(q => q.AcceptedQuantity)
                    : op.PurchaseOrderLineId.HasValue ? await _uow.Set<ServiceAcceptance>().AsNoTracking().Where(a => !a.Deleted && a.PurchaseOrderLineId == op.PurchaseOrderLineId.Value).SumAsync(a => a.Quantity, ct) : 0;
                if (accepted <= 0) return Bad<OperationDto>("پذیرش کیفیت کالای برگشتی لازم است: بازرسی ورودی سند برگشت با مقدار قبول‌شده، یا گواهی پذیرش خدمت سفارش خرید.");
                op.GoodQty = Math.Min(accepted, MfQuantityRules.Expected(loaded.All, op, order.Quantity));   // R1p D7: at most what the step before handed over
                op.FinishedAt = now;
                op.StartedAt ??= now;
                if (op.InspectionRequirement == MfInspectionRequirement.Qc || op.InspectionRequirement == MfInspectionRequirement.ItpPoint)
                {
                    op.Status = MfOperationStatus.QcHold;
                    op.QcRequestedAt = now;
                }
                else op.Status = MfOperationStatus.Done;
            }
            else if (action == "accept-by-concession" || action == "scrap-by-ncr")
            {
                // R1p D6: a rejected step goes on only on the decision of an approved non-conformance report: a concession lets the pieces continue, a scrap writes them off and drafts a replacement order
                bool concession = action == "accept-by-concession";
                if (!_user.IsInRole(ProductionRoles.Manager)) return Bad<OperationDto>("ادامه کار روی عملیات ردشده بر پایه گزارش عدم انطباق فقط با مدیر تولید است.");
                if (!dto.NcrId.HasValue) return Bad<OperationDto>("گزارش عدم انطباق (NCR) این عملیات را مشخص کنید.");
                if (op.Status != MfOperationStatus.Rejected) return Bad<OperationDto>("این کار فقط برای عملیاتی است که در بازرسی رد شده است.");
                NonConformance ncr = await _uow.Set<NonConformance>().FirstOrDefaultAsync(n => n.Id == dto.NcrId.Value && !n.Deleted, ct);
                if (ncr == null) return MfText.NotFound<OperationDto>("گزارش عدم انطباق پیدا نشد.");
                if (ncr.WorkOrderId != order.WorkOrderId || !op.NcrId.HasValue || op.NcrId.Value != ncr.Id) return Bad<OperationDto>("این گزارش عدم انطباق به همین عملیات مربوط نیست.");
                string ncrProblem = MfNcrRules.Check(ncr, concession ? NcrDisposition.Concession : NcrDisposition.Scrap);
                if (ncrProblem != null) return Bad<OperationDto>(ncrProblem);
                if (loaded.All.Any(o => o.IsRework && o.ReworkOfOperationId == op.Id && o.Status != MfOperationStatus.Rejected && o.Status != MfOperationStatus.Skipped))
                    return Bad<OperationDto>("برای این عملیات عملیات دوباره‌کاری ساخته شده است؛ ارفاق یا ضایعات روی عملیات دوباره‌کاری ثبت می‌شود.");
                if (concession)
                {
                    op.AcceptedByConcession = true;
                    op.Status = MfOperationStatus.Done;
                    op.FinishedAt ??= now;
                    if (reason != null) op.Note = (string.IsNullOrWhiteSpace(op.Note) ? string.Empty : op.Note + " | ") + "ارفاق: " + reason;
                }
                else
                {
                    decimal scrapped = ncr.Quantity > 0 ? Math.Min(ncr.Quantity, op.GoodQty) : op.GoodQty;
                    if (scrapped <= 0) return Bad<OperationDto>("تعداد سالمی که ضایعات شود روی این عملیات ثبت نشده است.");
                    // R1p D6/D33: the same write-off as the terminal scrap: the order's written-off quantity and planned serials follow, the replacement draft gets its own serials, and the scrap receipt is drafted after the save
                    var writeOff = new MfWriteOff { Order = order, Op = op, Quantity = scrapped, Job = JobCard(order.Number, op), Ncr = ncr, NcrIsNew = false };
                    string writeOffProblem = await MfWriteOffs.ApplyAsync(_uow, Me, writeOff, ncr.SerialNumbers, ct);
                    if (writeOffProblem != null) return Bad<OperationDto>(writeOffProblem);
                    writeOffs.Add(writeOff);
                    op.GoodQty -= scrapped;
                    op.ScrapQty += scrapped;
                    op.Status = MfOperationStatus.Done;
                    op.FinishedAt ??= now;
                    if (op.GoodQty == 0)
                    {
                        // nothing is left to make the later steps with
                        List<MfOperation> chain = MfFlow.Chain(loaded.All);
                        foreach (MfOperation later in chain.Skip(chain.FindIndex(o => o.Id == op.Id) + 1).Where(o => o.Status == MfOperationStatus.Waiting || o.Status == MfOperationStatus.Ready))
                        {
                            later.Status = MfOperationStatus.Skipped;
                            later.SkipReason = $"ضایعات کامل بر پایه گزارش عدم انطباق {ncr.Number}";
                            later.ModifyDate = now;
                            later.ModifierUserId = Me;
                        }
                    }
                }
            }
            else return Bad<OperationDto>("عمل نامعتبر است.");

            op.ModifyDate = now;
            op.ModifierUserId = Me;
            notes = await _flow.ApplyAsync(order, loaded.All, ct);
            notes.AddRange(actionNotes);
            await _flow.PartSyncAsync(op, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<OperationDto>(MfText.Concurrency);
            }
            notes.AddRange(await _flow.AfterSaveAsync(ct));
            notes.AddRange(await MfWriteOffs.AfterSaveAsync(_uow, _documents, _notify, writeOffs, ct));
            await MfGates.QcAsync(_notify, op, order.Number, ct);
            await MfGates.OrderAsync(_notify, order, ct);
            return ResponseDtoHelper.Ok(await OneAsync(op, ct), notes.Count == 0 ? null : Join(notes));
        }

        // ================================================================================== QC decision and rework

        public async Task<ResponseDto<OperationDto>> QcDecisionAsync(QcDecisionDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Inspectors)) return Bad<OperationDto>("تصمیم بازرسی فقط با بازرس کنترل کیفیت است.");
            if (dto == null || !Enum.IsDefined(typeof(QcResult), dto.Result)) return Bad<OperationDto>(MfText.InvalidData);
            var loaded = await LoadOperationAsync(dto.OperationId, ct);
            if (loaded.Error != null) return MfText.NotFound<OperationDto>(loaded.Error);
            MfOperation op = loaded.Op;
            MfProductionOrder order = loaded.Order;
            if (order.Status != MfOrderStatus.Released && order.Status != MfOrderStatus.InProgress) return Bad<OperationDto>("دستور تولید در حال اجرا نیست.");
            if (op.InspectionRequirement != MfInspectionRequirement.Qc && op.InspectionRequirement != MfInspectionRequirement.ItpPoint) return Bad<OperationDto>("این عملیات بازرسی QC ندارد.");
            if (op.Status != MfOperationStatus.QcHold) return Bad<OperationDto>("این عملیات در انتظار بازرسی نیست.");
            string note = Clean(dto.Note);
            if (dto.Result != QcResult.Accept && note == null) return Bad<OperationDto>("برای نتیجه غیر از «قبول»، توضیح بنویسید.");
            if (!order.ItemId.HasValue) return Bad<OperationDto>("کالای دستور تولید مشخص نیست.");

            // R1p D16: every measured value names the instrument it was measured with; the quality service then checks that the instrument is calibrated
            var measurements = new List<SaveQcMeasurementDto>();
            foreach (QcMeasurementInputDto m in dto.Measurements ?? new List<QcMeasurementInputDto>())
            {
                if (Clean(m.Characteristic) == null) return Bad<OperationDto>("نام مشخصه اندازه‌گیری‌شده را وارد کنید.");
                if (!m.InstrumentId.HasValue)
                    return Bad<OperationDto>($"برای مقدار اندازه‌گیری‌شده «{Clean(m.Characteristic)}» ابزار اندازه‌گیری را انتخاب کنید؛ مقداری که به ابزار کالیبره ردیابی نشود پذیرفته نمی‌شود.");
                measurements.Add(new SaveQcMeasurementDto { Characteristic = m.Characteristic, Nominal = m.Nominal, TolPlus = m.TolPlus, TolMinus = m.TolMinus, Measured = m.Measured, Unit = m.Unit, EquipmentId = m.InstrumentId });
            }

            // the inspector never inspects his own work: nobody who booked time on the operation is the signed-in inspector
            List<int> workers = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && b.OperationId == op.Id).Select(b => b.EmployeeId).Distinct().ToListAsync(ct);
            if (workers.Count > 0 && await _uow.Set<FnEmployee>().AsNoTracking().AnyAsync(e => workers.Contains(e.Id) && e.UserId == Me, ct))
                return Bad<OperationDto>(MfText.SameUser + " (بازرس نمی‌تواند روی کار خودش تصمیم بگیرد)");

            ItpActivity activity = null;
            if (op.InspectionRequirement == MfInspectionRequirement.ItpPoint)
            {
                int? activityId = dto.ItpActivityId ?? op.ItpActivityId;
                IQueryable<ItpActivity> activities = _uow.Set<ItpActivity>().Where(a => _uow.Set<Itp>().Any(i => i.Id == a.ItpId && i.WorkOrderId == order.WorkOrderId && !i.Deleted));
                activity = activityId.HasValue
                    ? await activities.FirstOrDefaultAsync(a => a.Id == activityId.Value, ct)
                    : await activities.Where(a => (int)a.TestType == op.ItpTestType).OrderBy(a => a.Status == ItpActivityStatus.Done || a.Status == ItpActivityStatus.Waived ? 0 : 1).ThenBy(a => a.Seq).FirstOrDefaultAsync(ct);
                if (activity == null) return Bad<OperationDto>("نقطه ITP این عملیات در برنامه بازرسی دستور کار پیدا نشد.");
                if (dto.Result == QcResult.Accept && activity.Status != ItpActivityStatus.Done && activity.Status != ItpActivityStatus.Waived && activity.Status != ItpActivityStatus.NotApplicable)
                    return Bad<OperationDto>($"نقطه ITP «{activity.Activity}» هنوز آزاد نشده است؛ ابتدا در ماژول کیفیت ثبت و آزاد شود.");
            }

            DateTimeOffset now = DateTimeOffset.Now;
            decimal quantity = op.GoodQty > 0 ? op.GoodQty : MfQuantityRules.Expected(loaded.All, op, order.Quantity);
            // R1p D42: the record (and its NCR) carries the lot or the serial numbers of the pieces
            (int? lotId, List<string> serials) = await MfTrace.OfOrderAsync(_uow, order, ct);

            // the inspection record (and, for a reject or rework, its NCR) is made by the quality service: its rules run (the ITP of the work order is ensured, the ITP point and the calibration rules apply)
            ResponseDto<QcRecordDto> saved = await _inspections.SaveRecordAsync(new SaveQcRecordDto
            {
                Type = QcRecordType.InProcess, WorkOrderId = order.WorkOrderId, ItpActivityId = activity?.Id, ItemId = order.ItemId, Quantity = quantity,
                Operation = JobCard(order.Number, op), RecordDate = DateTime.Today, Result = dto.Result, Note = note,
                LotId = lotId, SerialNumber = MfSerialRules.Joined(serials, 200), Measurements = measurements,
            }, ct);
            if (saved == null || !saved.Success || saved.Data == null) return Bad<OperationDto>(saved?.Message ?? MfText.InvalidData);
            QcRecordDto record = saved.Data;
            op.QcRecordId = record.Id;
            if (activity != null) op.ItpActivityId = activity.Id;

            switch (dto.Result)
            {
                case QcResult.Accept:
                    op.QcAccepted = true;
                    // R1p D38: the acceptance holds for the quantities QC saw now; if they change before the step finishes, QC has to accept again
                    op.QcAcceptedGoodQty = op.GoodQty;
                    op.QcAcceptedScrapQty = op.ScrapQty;
                    op.QcAcceptedReworkQty = op.ReworkQty;
                    op.Status = op.FinishedAt.HasValue ? MfOperationStatus.Done : MfOperationStatus.Paused;
                    break;
                case QcResult.Hold:
                    op.Note = note;
                    break;
                default:
                    {
                        if (!record.NcrId.HasValue) return Bad<OperationDto>("گزارش عدم انطباق برای این بازرسی ساخته نشد.");
                        op.NcrId = record.NcrId;
                        // R1p D42/D6: the report names every serial of the pieces under inspection, one per line (the inspection record keeps the shortened one-line text), so that a later scrap decision can tell which serials it takes
                        if (serials.Count > 0)
                        {
                            NonConformance qcNcr = await _uow.Set<NonConformance>().FirstOrDefaultAsync(n => n.Id == record.NcrId.Value, ct);
                            if (qcNcr != null) { qcNcr.SerialNumbers = MfSerialRules.PerLine(serials); qcNcr.ModifyDate = now; }
                        }
                        op.Status = MfOperationStatus.Rejected;
                        op.QcAccepted = false;
                        // R1p D8: a rework decision only marks the quantity; the rework operation is made after the NCR is approved with a rework disposition (operations/rework)
                        if (dto.Result == QcResult.Rework) op.ReworkQty = quantity;
                        break;
                    }
            }
            op.ModifyDate = now;
            op.ModifierUserId = Me;
            List<string> notes = await _flow.ApplyAsync(order, loaded.All, ct);
            await _flow.PartSyncAsync(op, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<OperationDto>(MfText.Concurrency);
            }
            notes.AddRange(await _flow.AfterSaveAsync(ct));
            await MfGates.QcAsync(_notify, op, order.Number, ct);
            return ResponseDtoHelper.Ok(await OneAsync(op, ct), notes.Count == 0 ? null : Join(notes));
        }

        private MfOperation NewReworkOperation(MfProductionOrder order, MfOperation origin, int? ncrId, DateTimeOffset now)
        {
            var rework = new MfOperation
            {
                OrderId = order.Id, Sequence = origin.Sequence, OperationTemplateId = origin.OperationTemplateId, Title = "دوباره‌کاری: " + origin.Title, WorkCenterId = origin.WorkCenterId,
                Status = MfOperationStatus.Waiting, InspectionRequirement = origin.InspectionRequirement == MfInspectionRequirement.None ? MfInspectionRequirement.Qc : origin.InspectionRequirement,
                ItpTestType = origin.ItpTestType, IsRework = true, ReworkOfOperationId = origin.Id, NcrId = ncrId, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<MfOperation>().Add(rework);
            return rework;
        }

        public async Task<ResponseDto<OperationDto>> ReworkAsync(ReworkOperationDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(ProductionRoles.Supervisors)) return Bad<OperationDto>(MfText.NotAllowed);
            if (dto == null) return Bad<OperationDto>(MfText.InvalidData);
            var loaded = await LoadOperationAsync(dto.OperationId, ct);
            if (loaded.Error != null) return MfText.NotFound<OperationDto>(loaded.Error);
            MfOperation op = loaded.Op;
            MfProductionOrder order = loaded.Order;
            if (order.Status != MfOrderStatus.Released && order.Status != MfOrderStatus.InProgress) return Bad<OperationDto>("دستور تولید در حال اجرا نیست.");
            if (op.Status != MfOperationStatus.Rejected || op.IsRework) return Bad<OperationDto>("دوباره‌کاری فقط برای عملیات ردشده در بازرسی ساخته می‌شود.");
            if (loaded.All.Any(o => o.IsRework && o.ReworkOfOperationId == op.Id && o.Status != MfOperationStatus.Rejected && o.Status != MfOperationStatus.Skipped)) return Bad<OperationDto>("برای این عملیات قبلاً عملیات دوباره‌کاری ساخته شده است.");
            NonConformance ncr = await _uow.Set<NonConformance>().FirstOrDefaultAsync(n => n.Id == dto.NcrId && !n.Deleted, ct);
            if (ncr == null) return MfText.NotFound<OperationDto>("گزارش عدم انطباق پیدا نشد.");
            if (ncr.WorkOrderId != order.WorkOrderId) return Bad<OperationDto>("این گزارش عدم انطباق به همین دستور کار مربوط نیست.");
            if (op.NcrId.HasValue && op.NcrId.Value != ncr.Id) return Bad<OperationDto>("این گزارش عدم انطباق به همین عملیات مربوط نیست.");
            // R1p D8: the rework operation exists only while the NCR is approved (quality, engineering and production) with a rework disposition
            string ncrProblem = MfNcrRules.Check(ncr, NcrDisposition.Rework);
            if (ncrProblem != null) return Bad<OperationDto>(ncrProblem);
            DateTimeOffset now = DateTimeOffset.Now;
            op.ReworkQty = op.ReworkQty > 0 ? op.ReworkQty : op.GoodQty > 0 ? op.GoodQty : MfQuantityRules.Expected(loaded.All, op, order.Quantity);
            op.NcrId ??= ncr.Id;
            MfOperation rework = NewReworkOperation(order, op, ncr.Id, now);
            if (Clean(dto.Note) != null) rework.Note = Clean(dto.Note);
            loaded.All.Add(rework);
            List<string> notes = await _flow.ApplyAsync(order, loaded.All, ct);
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Bad<OperationDto>(MfText.Concurrency);
            }
            // the NCR learns which operation did its rework
            ncr.ReworkOperationId = rework.Id;
            ncr.ModifyDate = DateTimeOffset.Now;
            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                notes.Add("پیوند عملیات دوباره‌کاری به گزارش عدم انطباق ثبت نشد.");
            }
            notes.AddRange(await _flow.AfterSaveAsync(ct));
            return ResponseDtoHelper.Ok(await OneAsync(rework, ct), notes.Count == 0 ? null : Join(notes));
        }
    }
}
