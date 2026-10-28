using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.InventoryDtos;

namespace Services.NasbNiro.Production
{
    /// <summary>One write-off of pieces of a production order: scrap reported on the terminal (D33) or scrap decided by an approved non-conformance report (D6).</summary>
    internal sealed class MfWriteOff
    {
        public MfProductionOrder Order;
        public MfOperation Op;
        public decimal Quantity;

        /// <summary>The job card (order number and operation) the scrap came from.</summary>
        public string Job;

        /// <summary>The serial numbers of the scrapped pieces (serial-tracked item with planned serials), else empty.</summary>
        public List<string> Serials = new List<string>();

        /// <summary>The non-conformance report of the scrap: drafted with the terminal scrap, the approved one with an NCR decision.</summary>
        public Entities.Model.NasbNiro.Inventory.NonConformance Ncr;

        /// <summary>True when the report was drafted by this write-off (terminal scrap); false for an existing, approved one.</summary>
        public bool NcrIsNew;

        /// <summary>The draft order that makes the pieces again.</summary>
        public MfProductionOrder Replacement;
    }

    /// <summary>
    /// R1p D6/D33: both ways a piece is written off (the terminal scrap and the scrap decision of an NCR) leave the same trace: the order's written-off quantity and its planned serials follow, a draft replacement order
    /// is made (with new planned serials for a serial-tracked item), the report is tied to the step, and the scrap receipt of the inventory module is drafted. ★ provisional decision: the replacement is always a draft
    /// and the receipt is always a draft; people release and confirm them.
    /// </summary>
    internal static class MfWriteOffs
    {
        /// <summary>
        /// Applies the write-off to the tracked entities (the order, the replacement draft); never saves. Returns the persian reason when the write-off cannot be applied (the serials of a serial-tracked item cannot be told),
        /// else null. <paramref name="namedSerials"/> are the scrapped serials as typed (one per line) or taken from the report.
        /// </summary>
        public static async Task<string> ApplyAsync(IUnitOfWork uow, int userId, MfWriteOff w, string namedSerials, CancellationToken ct)
        {
            MfProductionOrder order = w.Order;
            InventoryItem item = order.ItemId.HasValue ? await uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.ItemId.Value, ct) : null;
            List<string> planned = MfSerialRules.Planned(order.SerialsPlanned);
            List<string> replacementSerials = null;
            if (item != null && item.TrackingMode == ItemTrackingMode.Serial && planned.Count > 0)
            {
                (List<string> scrapped, string error) = MfSerialRules.Scrapped(planned, namedSerials, w.Quantity);
                if (error != null) return error;
                w.Serials = scrapped;
                List<string> remaining = MfSerialRules.Remaining(planned, scrapped);
                order.SerialsPlanned = remaining.Count == 0 ? null : MfSerialRules.PerLine(remaining);
                replacementSerials = await ReplacementSerialsAsync(uow, item.Id, scrapped, ct);
            }
            order.WrittenOffQty += w.Quantity;
            w.Replacement = await DraftReplacementAsync(uow, userId, order, w.Quantity, w.Ncr, replacementSerials, DateTimeOffset.Now, ct);
            return null;
        }

        /// <summary>New serials for the replacement pieces: the scrapped serial with "-R", unless that serial exists already (a unit of the item, or planned on another order).</summary>
        private static async Task<List<string>> ReplacementSerialsAsync(IUnitOfWork uow, int itemId, List<string> scrapped, CancellationToken ct)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string serial in scrapped)
            {
                string prefix = serial + "-R";
                foreach (string existing in await uow.Set<SerialUnit>().AsNoTracking().Where(u => u.ItemId == itemId && u.SerialNumber.StartsWith(prefix)).Select(u => u.SerialNumber).ToListAsync(ct))
                    taken.Add(existing);
                foreach (string planned in await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && o.ItemId == itemId && o.SerialsPlanned != null && o.SerialsPlanned.Contains(prefix)).Select(o => o.SerialsPlanned).ToListAsync(ct))
                    foreach (string p in MfSerialRules.Planned(planned)) taken.Add(p);
            }
            return MfSerialRules.Replacements(scrapped, taken.Contains);
        }

        /// <summary>
        /// The order that replaces the pieces a write-off took: a draft for the scrapped quantity (the components in proportion, the operations of the approved routing) that refers back to the order and the report.
        /// People release it as any other order; the inbox shows it as an order waiting for release.
        /// </summary>
        public static async Task<MfProductionOrder> DraftReplacementAsync(IUnitOfWork uow, int userId, MfProductionOrder order, decimal quantity, Entities.Model.NasbNiro.Inventory.NonConformance ncr, List<string> serials, DateTimeOffset now, CancellationToken ct)
        {
            var replacement = new MfProductionOrder
            {
                Number = await InventoryNumbers.NextNumberAsync(uow, MfNumbers.ProductionOrder, "PO-MF", DateTime.Today, ct),
                WorkOrderId = order.WorkOrderId, ContractId = order.ContractId, ProcessId = order.ProcessId, BomId = order.BomId, ActivityId = order.ActivityId, ItemId = order.ItemId,
                Quantity = quantity, Unit = order.Unit, Status = MfOrderStatus.Draft, AutoDrafted = true, ReplacesOrderId = order.Id,
                SerialsPlanned = serials == null || serials.Count == 0 ? null : MfSerialRules.PerLine(serials),
                Note = $"جایگزین {MfFlow.Num(quantity)} {order.Unit} ضایعات دستور تولید {order.Number}" + (ncr == null ? string.Empty : $" (گزارش عدم انطباق {ncr.Number})"),
                CreateDate = now, ModifyDate = now, CreatorUserId = userId, ModifierUserId = userId,
            };
            List<MfOrderComponent> comps = await uow.Set<MfOrderComponent>().AsNoTracking().Where(c => c.OrderId == order.Id).ToListAsync(ct);
            foreach (MfOrderComponent c in comps)
                replacement.Components.Add(new MfOrderComponent { ItemId = c.ItemId, Quantity = order.Quantity > 0 ? Math.Round(c.Quantity * quantity / order.Quantity, 4) : c.Quantity, Unit = c.Unit });
            InventoryItem item = order.ItemId.HasValue ? await uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == order.ItemId.Value, ct) : null;
            if (item != null)
            {
                MfResolvedRouting routing = await MfRouting.ResolveAsync(uow, item, ct);
                if (routing.Revision != null)
                {
                    replacement.RoutingRevisionId = routing.Revision.Id;
                    foreach (MfOperation op in MfDraftingService.OperationsFromRouting(routing, quantity, now, userId)) replacement.Operations.Add(op);
                }
            }
            uow.Set<MfProductionOrder>().Add(replacement);
            return replacement;
        }

        /// <summary>
        /// Call after the caller's save went through: the draft scrap receipt of each write-off (the scrap warehouse takes the scrapped pieces), the notes for the screen and the inbox item of each replacement order.
        /// A problem never fails the operation; it is returned as a note.
        /// </summary>
        public static async Task<List<string>> AfterSaveAsync(IUnitOfWork uow, IInventoryDocumentService documents, Office.INotificationService notify, IEnumerable<MfWriteOff> writeOffs, CancellationToken ct)
        {
            var notes = new List<string>();
            List<MfWriteOff> list = writeOffs.ToList();
            if (list.Count == 0) return notes;
            bool changed = false;
            Warehouse scrapWarehouse = await uow.Set<Warehouse>().AsNoTracking().Where(w => w.IsActive && !w.Deleted && w.Kind == WarehouseKind.Scrap).OrderBy(w => w.Id).FirstOrDefaultAsync(ct);
            foreach (MfWriteOff w in list)
            {
                if (w.Ncr != null && w.NcrIsNew)
                {
                    notes.Add($"گزارش عدم انطباق پیش‌نویس {w.Ncr.Number} برای ضایعات عملیات {w.Job} ساخته شد.");
                    // R1p review 12: the report of a terminal scrap is tied to the step (a step that already has a report of its own, from an inspection, keeps that one)
                    if (w.Op != null && !w.Op.NcrId.HasValue && w.Ncr.Id > 0)
                    {
                        w.Op.NcrId = w.Ncr.Id;
                        w.Op.ModifyDate = DateTimeOffset.Now;
                        changed = true;
                    }
                }
                if (w.Replacement != null)
                {
                    notes.Add($"دستور تولید جایگزین {w.Replacement.Number} (پیش‌نویس) برای {MfFlow.Num(w.Quantity)} {w.Order.Unit} ساخته شد.");
                    await MfGates.OrderAsync(notify, w.Replacement, ct);
                }
                string problem = null;
                InventoryItem item = w.Order.ItemId.HasValue ? await uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == w.Order.ItemId.Value, ct) : null;
                if (item == null) problem = "کالای دستور تولید مشخص نیست.";
                else if (scrapWarehouse == null) problem = "انبار ضایعات تعریف نشده است.";
                else
                {
                    var line = new SaveDocumentLineDto { ItemId = item.Id, Quantity = w.Quantity, Description = $"ضایعات عملیات {w.Job}" };
                    if (item.TrackingMode == ItemTrackingMode.Lot) line.LotNumber = w.Order.Number;
                    else if (item.TrackingMode == ItemTrackingMode.Serial)
                    {
                        if (w.Serials.Count == 0 || w.Serials.Count != (int)w.Quantity) problem = "سریال قطعه ضایعاتی مشخص نیست؛ رسید ضایعات را پس از مشخص شدن سریال‌ها دستی ثبت کنید.";
                        else line.SerialNumbers = w.Serials.ToList();
                    }
                    if (problem == null)
                    {
                        ResponseDto<DocumentDto> saved = await documents.SaveDraftAsync(new SaveDocumentDto
                        {
                            Type = InventoryDocumentType.ScrapReceipt, DocumentDate = DateTime.Today, WarehouseId = scrapWarehouse.Id, WorkOrderId = w.Order.WorkOrderId, ContractId = w.Order.ContractId,
                            ReferenceNumber = w.Ncr?.Number ?? w.Job, Description = $"رسید ضایعات کارگاه - عملیات {w.Job}", Lines = new List<SaveDocumentLineDto> { line },
                        }, ct);
                        if (saved != null && saved.Success && saved.Data != null)
                        {
                            // a draft inventory document has no number until the warehouse confirms it: name it by the report it quotes (its reference), else by its id
                            string draftName = saved.Data.Number ?? (w.Ncr != null ? $"مربوط به گزارش {w.Ncr.Number}" : $"شناسه {saved.Data.Id}");
                            notes.Add($"رسید ضایعات پیش‌نویس ({draftName}) در انبار ضایعات ساخته شد؛ انباردار آن را بررسی و قطعی کند.");
                            if (w.Ncr != null)
                            {
                                w.Ncr.Description = (w.Ncr.Description ?? string.Empty) + $" | رسید ضایعات پیش‌نویس شناسه {saved.Data.Id}";
                                w.Ncr.ModifyDate = DateTimeOffset.Now;
                                changed = true;
                            }
                        }
                        else problem = saved?.Message ?? "پیش‌نویس رسید ضایعات ساخته نشد.";
                    }
                }
                if (problem != null) notes.Add($"رسید ضایعات عملیات {w.Job} ساخته نشد: {problem}");
            }
            if (changed)
            {
                try
                {
                    await uow.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    notes.Add("پیوند رسید ضایعات به گزارش عدم انطباق ثبت نشد.");
                }
            }
            return notes;
        }
    }
}
