using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;

namespace Services.NasbNiro.AfterSales
{
    /// <summary>The system's draft of the installed base: one unit for each delivered serial unit of a shipment that reached HandoverReceived.</summary>
    internal static class SvUnitDraft
    {
        /// <summary>R1c (M5): the commissioning date of a shipment is recorded: the units that service has not confirmed yet get the warranty end of the terms again (a confirmed unit is service's decision and is left as it is).</summary>
        public static async Task RefreshWarrantyAsync(IUnitOfWork uow, PcShipment shipment, CancellationToken ct)
        {
            PcContractTerms terms = await uow.Set<PcContractTerms>().AsNoTracking().FirstOrDefaultAsync(t => t.ContractId == shipment.ContractId && !t.Deleted, ct);
            if (terms == null) return;
            DateTime? end = PcWarranty.End(shipment.CommissionedAt, shipment.DeliveredAt ?? shipment.HandoverAt, terms.WarrantyMonthsAfterCommissioning, terms.WarrantyMonthsAfterDelivery);
            if (!end.HasValue) return;
            foreach (SvInstalledUnit u in await uow.Set<SvInstalledUnit>().Where(u => u.ShipmentId == shipment.Id && !u.Deleted && !u.IsConfirmed && u.WarrantySource != SvWarrantySource.Manual).ToListAsync(ct))
            {
                u.WarrantyEnd = end.Value.Date; u.WarrantySource = SvWarrantySource.Terms; if (!u.WarrantyStart.HasValue) u.WarrantyStart = (shipment.HandoverAt ?? shipment.DeliveredAt)?.Date;
            }
        }

        /// <summary>Adds the draft units to the context (the caller saves in the same save as the shipment). Idempotent: a shipment that has units gets none.</summary>
        public static async Task<int> DraftAsync(IUnitOfWork uow, PcShipment shipment, int userId, CancellationToken ct)
        {
            if (await uow.Set<SvInstalledUnit>().AnyAsync(u => u.ShipmentId == shipment.Id && !u.Deleted, ct)) return 0;
            int? customerId = await uow.Set<SlCustomerLink>().AsNoTracking()
                .Where(l => l.ContractId == shipment.ContractId && !l.Deleted).Select(l => (int?)l.CustomerId).FirstOrDefaultAsync(ct);
            string woCode = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == shipment.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            // R1c (M8): when the shipment names its serial units (lines), those are the units of the shipment; a shipment of before the lines existed takes all the delivered serials of the work order
            List<int> shipped = await uow.Set<PcShipmentLine>().AsNoTracking().Where(l => l.ShipmentId == shipment.Id && l.SerialUnitId != null).Select(l => l.SerialUnitId.Value).ToListAsync(ct);
            IQueryable<SerialUnit> serialQuery = uow.Set<SerialUnit>().AsNoTracking().Include(s => s.Item)
                .Where(s => s.WorkOrderId == shipment.WorkOrderId && s.ParentSerialUnitId == null && s.Status == SerialUnitStatus.Delivered && !s.Deleted);
            if (shipped.Count > 0) serialQuery = serialQuery.Where(s => shipped.Contains(s.Id));
            List<SerialUnit> serials = await serialQuery.OrderBy(s => s.Id).ToListAsync(ct);

            DateTime handover = (shipment.HandoverAt ?? shipment.DeliveredAt ?? DateTime.Today).Date;
            // R1c (M16): the warranty of the unit is counted from the contract terms (the earlier of commissioning + months and delivery + months), not from the expiry of the bank guarantee
            PcContractTerms terms = await uow.Set<PcContractTerms>().AsNoTracking().FirstOrDefaultAsync(t => t.ContractId == shipment.ContractId && !t.Deleted, ct);
            // R1c (M5): the commissioning date is its own fact (unknown until recorded: then only delivery + months applies); the handover is not the commissioning
            DateTime? warrantyEnd = terms == null ? null : PcWarranty.End(shipment.CommissionedAt, shipment.DeliveredAt ?? shipment.HandoverAt, terms.WarrantyMonthsAfterCommissioning, terms.WarrantyMonthsAfterDelivery);

            DateTimeOffset now = DateTimeOffset.Now;
            int made = 0;
            List<SerialUnit> list = serials.Count > 0 ? serials : new List<SerialUnit> { null };
            foreach (SerialUnit s in list)
            {
                string number = await InventoryNumbers.NextNumberAsync(uow, SvNumbers.Unit, "SVU", handover, ct);
                var unit = new SvInstalledUnit
                {
                    Tag = number, Title = s != null ? $"{s.Item?.Title} {s.SerialNumber}".Trim() : $"محموله {shipment.Number} دستور کار {woCode}",
                    SerialUnitId = s?.Id, CustomerId = customerId, ContractId = shipment.ContractId, WorkOrderId = shipment.WorkOrderId, ShipmentId = shipment.Id,
                    HandoverDate = handover, WarrantyStart = warrantyEnd.HasValue ? handover : (DateTime?)null, WarrantyEnd = warrantyEnd?.Date,
                    WarrantySource = warrantyEnd.HasValue ? SvWarrantySource.Terms : SvWarrantySource.None,
                    IsConfirmed = false, EditorUserIds = null, CreateDate = now, CreatorUserId = userId, ModifyDate = now, ModifierUserId = userId,
                    Note = "پیش‌نویس سیستم از تحویل محموله؛ سرویس سایت، شماره tag و گارانتی را تأیید می‌کند.",
                };
                uow.Set<SvInstalledUnit>().Add(unit);
                made++;
            }
            return made;
        }
    }
}
