using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Providers;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Production
{
    /// <summary>
    /// The rules that move the operations of a released order: which operation is ready, the outside work drafts and the completion with the production receipt draft.
    /// It changes tracked entities and never saves: the caller validates first and saves. The two document drafts (the issue to the contractor and the production receipt) go through the
    /// inventory service, which saves by itself, so they are made by <see cref="AfterSaveAsync"/> after the caller's guarded save.
    /// </summary>
    internal class MfFlow
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInventoryDocumentService _documents;
        private readonly INotificationService _notify;

        /// <summary>Outside-work drafts and receipts that need a document of the inventory module; they are made by <see cref="AfterSaveAsync"/> once the caller's own save went through.</summary>
        private readonly List<(MfProductionOrder Order, MfOperation Op, PurchaseRequest Request, decimal Quantity)> _pendingIssues = new List<(MfProductionOrder, MfOperation, PurchaseRequest, decimal)>();
        private readonly List<(MfProductionOrder Order, List<MfOperation> Chain)> _pendingReceipts = new List<(MfProductionOrder, List<MfOperation>)>();

        public MfFlow(IUnitOfWork uow, IInventoryUserContext user, IInventoryDocumentService documents, INotificationService notify = null)
        {
            _uow = uow;
            _user = user;
            _documents = documents;
            _notify = notify;
        }

        public static List<MfOperation> Chain(IEnumerable<MfOperation> ops) =>
            ops.Where(o => !o.Deleted).OrderBy(o => o.Sequence).ThenBy(o => o.IsRework ? 1 : 0).ThenBy(o => o.Id).ToList();

        /// <summary>The kit (material request) is fully issued, or there is no kit, or a supervisor allowed the start with a reason.</summary>
        public async Task<bool> KitReadyAsync(MfProductionOrder order, CancellationToken ct)
        {
            if (order.KitRequest == null && !order.KitRequestId.HasValue) return true;
            if (!string.IsNullOrWhiteSpace(order.KitOverrideReason)) return true;
            if (order.KitRequest != null && order.KitRequest.Id == 0) return false;   // just drafted: nothing is issued yet
            MaterialRequest request = await _uow.Set<MaterialRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == (order.KitRequestId ?? order.KitRequest.Id), ct);
            if (request == null || request.Deleted || request.Status == MaterialRequestStatus.Rejected) return false;
            List<MaterialRequestLine> lines = await _uow.Set<MaterialRequestLine>().AsNoTracking().Where(l => l.MaterialRequestId == request.Id).ToListAsync(ct);
            return lines.Count > 0 && lines.All(l => l.IssuedQuantity >= l.Quantity);
        }

        private static bool PreviousAllows(MfOperation previous, MfOperation op) =>
            previous.Status == MfOperationStatus.Skipped
            || previous.Status == MfOperationStatus.Done
            || (op.IsRework && op.ReworkOfOperationId == previous.Id && previous.Status == MfOperationStatus.Rejected);

        /// <summary>True when every operation is finished (a rejected one only counts when its rework operation is done) and at least one was done.</summary>
        public static bool IsComplete(List<MfOperation> chain)
        {
            if (chain.Count == 0 || !chain.Any(o => o.Status == MfOperationStatus.Done)) return false;
            foreach (MfOperation o in chain)
            {
                if (o.Status == MfOperationStatus.Done || o.Status == MfOperationStatus.Skipped) continue;
                if (o.Status == MfOperationStatus.Rejected && chain.Any(r => r.IsRework && r.ReworkOfOperationId == o.Id && r.Status == MfOperationStatus.Done)) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Recomputes Waiting/Ready of the operations of a released order, drafts the outside work of operations that became ready and completes the order when
        /// the last operation is done. Returns the notes (Persian) about drafts that could not be made.
        /// </summary>
        public async Task<List<string>> ApplyAsync(MfProductionOrder order, List<MfOperation> ops, CancellationToken ct)
        {
            var notes = new List<string>();
            if (order.Status != MfOrderStatus.Released && order.Status != MfOrderStatus.InProgress) return notes;
            List<MfOperation> chain = Chain(ops);
            bool kitReady = await KitReadyAsync(order, ct);
            DateTimeOffset now = DateTimeOffset.Now;
            MfOperation previous = null;
            foreach (MfOperation op in chain)
            {
                if (op.Status == MfOperationStatus.Waiting || op.Status == MfOperationStatus.Ready)
                {
                    bool ok = previous == null ? kitReady : PreviousAllows(previous, op);
                    MfOperationStatus wanted = ok ? MfOperationStatus.Ready : MfOperationStatus.Waiting;
                    if (op.Status != wanted)
                    {
                        op.Status = wanted;
                        op.ModifyDate = now;
                        op.ModifierUserId = _user.UserId;
                    }
                    if (ok && op.IsOutside && !op.OutsideDrafted)
                    {
                        string problem = await DraftOutsideAsync(order, op, chain, ct);
                        if (problem != null) notes.Add(problem);
                    }
                }
                previous = op;
            }

            if (order.Status == MfOrderStatus.Released && chain.Any(o => o.StartedAt.HasValue))
            {
                order.Status = MfOrderStatus.InProgress;
                order.ActualStart ??= chain.Where(o => o.StartedAt.HasValue).Min(o => o.StartedAt);
            }
            if (IsComplete(chain))
            {
                order.Status = MfOrderStatus.Completed;
                order.ActualFinish = now;
                if (!order.ProductionReceiptId.HasValue && !_pendingReceipts.Any(r => r.Order == order)) _pendingReceipts.Add((order, chain));
            }
            order.ModifyDate = now;
            order.ModifierUserId = _user.UserId;
            return notes;
        }

        /// <summary>Keeps the PartOperation rows of the traceability screens in step with the operation of the order.</summary>
        public async Task PartSyncAsync(MfOperation op, CancellationToken ct)
        {
            List<int> ids = await _uow.Set<MfOperationPart>().AsNoTracking().Where(p => p.OperationId == op.Id).Select(p => p.PartOperationId).ToListAsync(ct);
            if (ids.Count == 0) return;
            List<PartOperation> parts = await _uow.Set<PartOperation>().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (PartOperation p in parts)
            {
                switch (op.Status)
                {
                    case MfOperationStatus.InProgress:
                    case MfOperationStatus.Paused:
                        p.Status = PartOperationStatus.InProgress;
                        p.StartedAt ??= op.StartedAt ?? now;
                        break;
                    case MfOperationStatus.QcHold:
                        p.Status = PartOperationStatus.InProgress;
                        p.QcStatus = QcStatus.Pending;
                        break;
                    case MfOperationStatus.Done:
                        p.Status = PartOperationStatus.Done;
                        p.FinishedAt = op.FinishedAt ?? now;
                        if (op.InspectionRequirement == MfInspectionRequirement.Qc || op.InspectionRequirement == MfInspectionRequirement.ItpPoint) { p.QcStatus = QcStatus.Accepted; p.QcAt = now; }
                        break;
                    case MfOperationStatus.Rejected:
                        p.Status = PartOperationStatus.Rejected;
                        p.QcStatus = QcStatus.Rejected;
                        p.QcAt = now;
                        break;
                }
                p.ModifyDate = now;
                p.ModifierUserId = _user.UserId;
            }
        }

        // ================================================================================== outside work

        /// <summary>Drafts the purchase request of an outside operation that became ready; returns the persian reason when nothing is drafted (the step before handed over no good piece).</summary>
        private async Task<string> DraftOutsideAsync(MfProductionOrder order, MfOperation op, List<MfOperation> chain, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            string job = MfNames.Operation(order.Number, op.Sequence);

            // R1p D7: what goes to the contractor is what the operation before it finished (good pieces), not the whole order quantity again after earlier scrap or a write-off;
            // when the step before produced nothing there is nothing to send, and no request is drafted for pieces that do not exist
            decimal quantity = MfQuantityRules.Expected(chain, op, order.Quantity);
            if (quantity <= 0)
                return $"کار بیرونی عملیات {job} آماده شد ولی مرحله‌ی قبل هیچ قطعه‌ی سالمی تحویل نداده است؛ درخواست خرید و حواله ارسال به پیمانکار ساخته نشد.";

            // purchase request: a subcontract line, drafted for procurement (existing approvals apply). It is only added to the context here; the caller's save stores it with the operation.
            Provider supplier = op.OutsideSupplierId.HasValue ? await _uow.Set<Provider>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == op.OutsideSupplierId.Value, ct) : null;
            var request = new PurchaseRequest
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, ProcurementNumbers.PurchaseRequest, "PRQ", DateTime.Today, ct),
                Source = PurchaseRequestSource.AdHoc, Kind = PurchaseKind.Subcontract, Status = PurchaseRequestStatus.Draft, RequestDate = DateTime.Today,
                WorkOrderId = order.WorkOrderId, ContractId = order.ContractId,
                Description = $"کار بیرونی عملیات {job} ({op.Title})" + (supplier?.Name == null ? string.Empty : $" - پیمانکار پیشنهادی: {supplier.Name}"),
                CreateDate = now, ModifyDate = now, CreatorUserId = _user.UserId, ModifierUserId = _user.UserId,
                Lines = new List<PurchaseRequestLine>(),
            };
            request.Lines.Add(new PurchaseRequestLine
            {
                RowNumber = 1, Description = $"{op.Title} - دستور تولید {order.Number}", Unit = string.IsNullOrWhiteSpace(order.Unit) ? "عدد" : order.Unit, Quantity = quantity,
                NeededBy = DateTime.Today.AddDays(Math.Max(op.OutsideLeadDays, 0)),
                TechnicalRequirements = supplier?.Name == null ? null : $"پیمانکار پیشنهادی طبق استاندارد عملیات: {supplier.Name}",
            });
            _uow.Set<PurchaseRequest>().Add(request);
            op.PurchaseRequest = request;
            op.PurchaseRequestLine = request.Lines.First();
            _pendingIssues.Add((order, op, request, quantity));
            op.OutsideDrafted = true;
            return null;
        }

        /// <summary>
        /// Call after the caller's save went through: makes the issue-to-contractor draft of the outside work that became ready, raises the procurement inbox item of its purchase
        /// request and drafts the production receipt of an order that completed. A problem never fails the operation: it is written on the operation and returned as a note.
        /// </summary>
        public async Task<List<string>> AfterSaveAsync(CancellationToken ct)
        {
            var notes = new List<string>();
            bool changed = false;
            foreach (var pending in _pendingIssues.ToList())
            {
                MfProductionOrder order = pending.Order;
                MfOperation op = pending.Op;
                string job = MfNames.Operation(order.Number, op.Sequence);
                string problem = null;
                if (pending.Request.Id > 0)
                    await OfficeGates.SyncAsync(_notify, "pr:" + pending.Request.Id, true, "procurement", "purchase-request", pending.Request.Id,
                        $"درخواست خرید پیش‌نویس کار بیرونی {job} منتظر بررسی و ارسال است", "/procurement/requests/" + pending.Request.Id, ProcurementRoles.Commercial, ct);

                // issue to the contractor: a draft document of the existing inventory module
                if (!order.ItemId.HasValue) problem = "کالای دستور تولید مشخص نیست.";
                else if (!op.OutsideSupplierId.HasValue) problem = "پیمانکار مرحله بیرونی تعیین نشده است.";
                else
                {
                    Provider provider = await _uow.Set<Provider>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == op.OutsideSupplierId.Value, ct);
                    List<Warehouse> warehouses = await _uow.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive && !w.Deleted).OrderBy(w => w.Id).ToListAsync(ct);
                    List<Warehouse> contractors = warehouses.Where(w => w.Kind == WarehouseKind.Subcontractor).ToList();
                    Warehouse destination = contractors.Count == 1 ? contractors[0]
                        : contractors.FirstOrDefault(w => provider != null && provider.Name != null && ((w.Title ?? string.Empty).Contains(provider.Name) || (w.Code ?? string.Empty).Contains(provider.Name)));
                    Warehouse source = warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.WorkInProgress) ?? warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.Parts)
                        ?? warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.General);
                    if (destination == null) problem = "انبار مجازی پیمانکار پیدا نشد.";
                    else if (source == null) problem = "انبار مبدا ارسال پیدا نشد.";
                    else
                    {
                        ResponseDto<DocumentDto> saved = await _documents.SaveDraftAsync(new SaveDocumentDto
                        {
                            Type = InventoryDocumentType.SubcontractIssue, DocumentDate = DateTime.Today, WarehouseId = source.Id, DestinationWarehouseId = destination.Id,
                            ProviderId = op.OutsideSupplierId, WorkOrderId = order.WorkOrderId, ContractId = order.ContractId, ReferenceNumber = job,
                            Description = $"ارسال به پیمانکار برای عملیات {job} ({op.Title})",
                            Lines = new List<SaveDocumentLineDto> { new SaveDocumentLineDto { ItemId = order.ItemId.Value, Quantity = pending.Quantity, Description = op.Title } },
                        }, ct);
                        if (saved != null && saved.Success && saved.Data != null) { op.SubcontractIssueId = saved.Data.Id; changed = true; }
                        else problem = saved?.Message ?? "پیش‌نویس حواله ارسال ساخته نشد.";
                    }
                }
                if (problem != null)
                {
                    string note = $"پیش‌نویس حواله ارسال به پیمانکار ساخته نشد: {problem}";
                    op.Note = string.IsNullOrWhiteSpace(op.Note) ? note : op.Note + " | " + note;
                    notes.Add($"عملیات {job}: {note}");
                    changed = true;
                }
            }
            _pendingIssues.Clear();

            foreach (var pending in _pendingReceipts.ToList())
            {
                if (await DraftReceiptAsync(pending.Order, pending.Chain, notes, ct)) changed = true;
            }
            _pendingReceipts.Clear();

            if (changed)
            {
                try
                {
                    await _uow.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    notes.Add("پیوند حواله یا رسید ساخته‌شده به عملیات ثبت نشد؛ دوباره «به‌روزرسانی آمادگی» را بزنید.");
                }
            }
            return notes;
        }

        // ================================================================================== completion

        private async Task<bool> DraftReceiptAsync(MfProductionOrder order, List<MfOperation> chain, List<string> notes, CancellationToken ct)
        {
            if (order.ProductionReceiptId.HasValue) return false;
            if (!order.ItemId.HasValue) { notes.Add("رسید ساخت ساخته نشد: کالای دستور تولید مشخص نیست."); return false; }
            MfOperation last = chain.LastOrDefault(o => o.Status == MfOperationStatus.Done);
            decimal quantity = last?.GoodQty ?? 0;
            if (quantity <= 0) { notes.Add("رسید ساخت ساخته نشد: تعداد سالم گزارش‌شده در آخرین عملیات صفر است."); return false; }
            InventoryItem item = await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.ItemId.Value, ct);
            if (item == null) { notes.Add("رسید ساخت ساخته نشد: کالا پیدا نشد."); return false; }

            ProcessType? type = order.ProcessId.HasValue
                ? await _uow.Set<ProcessEntity>().AsNoTracking().Where(p => p.Id == order.ProcessId.Value).Select(p => (ProcessType?)p.Type).FirstOrDefaultAsync(ct)
                : null;
            bool assembled = type == ProcessType.Assembly || type == ProcessType.FinalAssembly;
            List<Warehouse> warehouses = await _uow.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive && !w.Deleted).OrderBy(w => w.Id).ToListAsync(ct);
            Warehouse target = assembled
                ? warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.FinishedGoods) ?? warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.WorkInProgress)
                : warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.WorkInProgress) ?? warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.FinishedGoods);
            target ??= warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.Parts) ?? warehouses.FirstOrDefault(w => w.Kind == WarehouseKind.General);
            if (target == null) { notes.Add("رسید ساخت ساخته نشد: انبار مقصد (کار در جریان یا کالای ساخته‌شده) پیدا نشد."); return false; }

            var line = new SaveDocumentLineDto { ItemId = item.Id, Quantity = quantity, Description = $"دستور تولید {order.Number}" };
            if (item.TrackingMode == ItemTrackingMode.Lot) line.LotNumber = order.Number;
            else if (item.TrackingMode == ItemTrackingMode.Serial)
            {
                // R1p D6/D33: scrapped pieces left the planned serials when they were written off, so the planned serials are the serials of the good pieces
                List<string> serials = MfSerialRules.Planned(order.SerialsPlanned);
                if (serials.Count != (int)quantity) { notes.Add($"رسید ساخت ساخته نشد: تعداد سریال‌های برنامه‌ریزی‌شده ({serials.Count}) با تعداد سالم ({Num(quantity)}) برابر نیست؛ سریال‌های دستور را اصلاح کنید."); return false; }
                line.SerialNumbers = serials;
            }
            ResponseDto<DocumentDto> saved = await _documents.SaveDraftAsync(new SaveDocumentDto
            {
                Type = InventoryDocumentType.ProductionReceipt, DocumentDate = DateTime.Today, WarehouseId = target.Id, WorkOrderId = order.WorkOrderId, ContractId = order.ContractId,
                ReferenceNumber = order.Number, Description = $"رسید ساخت دستور تولید {order.Number}", Lines = new List<SaveDocumentLineDto> { line },
            }, ct);
            if (saved != null && saved.Success && saved.Data != null)
            {
                order.ProductionReceiptId = saved.Data.Id;
                return true;
            }
            notes.Add("رسید ساخت ساخته نشد: " + (saved?.Message ?? "خطای نامشخص"));
            return false;
        }

        // ================================================================================== need date and priority

        public sealed class Urgency
        {
            public int? TotalFloat { get; set; }
            public DateTime? NeedDate { get; set; }
            public int Priority { get; set; }
        }

        /// <summary>Priority 1 is the most urgent: CPM total float ascending, then the need date, then the order number. Only the open orders are ranked.</summary>
        public static async Task<Dictionary<int, Urgency>> UrgencyAsync(IUnitOfWork uow, CancellationToken ct)
        {
            var rows = await (from o in uow.Set<MfProductionOrder>().AsNoTracking()
                              where !o.Deleted
                              join a in uow.Set<Entities.Model.NasbNiro.ProjectControl.PcActivity>().AsNoTracking() on o.ActivityId equals a.Id into ag
                              from a in ag.DefaultIfEmpty()
                              join w in uow.Set<WorkOrder>().AsNoTracking() on o.WorkOrderId equals w.Id
                              select new { o.Id, o.Number, o.Status, Float = a == null ? (int?)null : a.TotalFloat, Late = a == null ? (DateTime?)null : a.LateFinish, Start = w.StartDate })
                .ToListAsync(ct);
            var items = rows.Select(r => new { r.Id, r.Number, r.Status, r.Float, Need = r.Late ?? r.Start?.DateTime }).ToList();
            var ordered = items.Where(x => x.Status == MfOrderStatus.Draft || x.Status == MfOrderStatus.Released || x.Status == MfOrderStatus.InProgress).OrderBy(x => x.Float.HasValue ? 0 : 1).ThenBy(x => x.Float ?? 0).ThenBy(x => x.Need.HasValue ? 0 : 1).ThenBy(x => x.Need ?? DateTime.MaxValue).ThenBy(x => x.Number, StringComparer.Ordinal).ToList();
            var result = new Dictionary<int, Urgency>();
            int rank = 1;
            foreach (var x in ordered) result[x.Id] = new Urgency { TotalFloat = x.Float, NeedDate = x.Need, Priority = rank++ };
            foreach (var x in items.Where(i => !result.ContainsKey(i.Id))) result[x.Id] = new Urgency { TotalFloat = x.Float, NeedDate = x.Need, Priority = 0 };
            return result;
        }

        public static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
