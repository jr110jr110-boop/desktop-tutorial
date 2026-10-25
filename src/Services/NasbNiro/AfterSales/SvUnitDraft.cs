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

namespace Services.NasbNiro.AfterSales
{
    /// <summary>The system's draft of the installed base: one unit for each delivered serial unit of a shipment that reached HandoverReceived.</summary>
    internal static class SvUnitDraft
    {
        /// <summary>Adds the draft units to the context (the caller saves in the same save as the shipment). Idempotent: a shipment that has units gets none.</summary>
        public static async Task<int> DraftAsync(IUnitOfWork uow, PcShipment shipment, int userId, CancellationToken ct)
        {
            if (await uow.Set<SvInstalledUnit>().AnyAsync(u => u.ShipmentId == shipment.Id && !u.Deleted, ct)) return 0;
            int? customerId = await uow.Set<SlCustomerLink>().AsNoTracking()
                .Where(l => l.ContractId == shipment.ContractId && !l.Deleted).Select(l => (int?)l.CustomerId).FirstOrDefaultAsync(ct);
            string woCode = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == shipment.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            List<SerialUnit> serials = await uow.Set<SerialUnit>().AsNoTracking().Include(s => s.Item)
                .Where(s => s.WorkOrderId == shipment.WorkOrderId && s.ParentSerialUnitId == null && s.Status == SerialUnitStatus.Delivered && !s.Deleted).OrderBy(s => s.Id).ToListAsync(ct);

            DateTime handover = (shipment.HandoverAt ?? shipment.DeliveredAt ?? DateTime.Today).Date;
            DateTime? guaranteeEnd = await uow.Set<PcGuaranteeInfo>().AsNoTracking()
                .Where(g => g.ContractId == shipment.ContractId && g.GuaranteeType == PcGuaranteeType.Warranty && g.ExpiryDate != null && g.Status != PcGuaranteeStatus.Cancelled && !g.Deleted)
                .OrderByDescending(g => g.ExpiryDate).Select(g => g.ExpiryDate).FirstOrDefaultAsync(ct);

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
                    HandoverDate = handover, WarrantyStart = guaranteeEnd.HasValue ? handover : (DateTime?)null, WarrantyEnd = guaranteeEnd?.Date,
                    WarrantySource = guaranteeEnd.HasValue ? SvWarrantySource.Guarantee : SvWarrantySource.None,
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
