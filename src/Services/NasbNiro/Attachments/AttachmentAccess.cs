using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Attachments;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;

namespace Services.NasbNiro.Attachments
{
    /// <summary>PB3: the read rule of a stored file that an attachment row holds. Used by the file access check (FileAccess), so a download follows the owner's rules.</summary>
    public static class AttachmentAccess
    {
        /// <summary>
        /// null = no attachment row holds the file (the other rules decide); true / false = the signed-in user may / may not read it through the attachments.
        /// A file of a price-carrying kind (invoice, payment slip) is hidden from everybody outside the price readers. The technical documents of a purchase request are read by everyone who may read
        /// a record downstream of it (P3); any other attachment by the readers of its owner record.
        /// </summary>
        public static async Task<bool?> CanReadFileAsync(IUnitOfWork uow, IInventoryUserContext user, Guid documentId, CancellationToken ct)
        {
            List<RecordAttachment> rows = await uow.Set<RecordAttachment>().AsNoTracking().IgnoreQueryFilters().Where(a => a.DocumentId == documentId && !a.Deleted).ToListAsync(ct);
            if (rows.Count == 0) return null;
            bool seesPrices = user.IsInRole(AttachmentRoles.Prices);
            foreach (RecordAttachment row in rows)
            {
                if (AttachmentRules.CarriesPrices(row.Kind) && !seesPrices) continue;
                OwnerFacts facts = await AttachmentOwners.FactsAsync(uow, row.OwnerType, row.OwnerId, ct);
                if (!facts.Exists) continue;
                bool technicalOfRequest = (row.OwnerType == AttachmentOwnerType.PurchaseRequest || row.OwnerType == AttachmentOwnerType.PurchaseRequestLine) && !AttachmentRules.CarriesPrices(row.Kind);
                if (!technicalOfRequest)
                {
                    if (facts.ReadRoles == null || user.IsInRole(facts.ReadRoles)) return true;
                    continue;
                }
                // the technical files of a request: the procurement readers read them; the shop floor and security only through a record downstream of that request that exists and that they read
                if (user.IsInRole(AttachmentRoles.Procurement)) return true;
                if (user.IsInRole(AttachmentRoles.Operational) && await HasOperationalRecordAsync(uow, facts.PurchaseRequestIds, ct)) return true;
            }
            return false;
        }

        /// <summary>A warehouse document (receipt, inspection, NCR hang on one) or a production operation made for one of the purchase requests.</summary>
        private static async Task<bool> HasOperationalRecordAsync(IUnitOfWork uow, HashSet<int> requestIds, CancellationToken ct)
        {
            if (requestIds.Count == 0) return false;
            List<int> prs = requestIds.ToList();
            if (await uow.Set<Entities.Model.NasbNiro.Production.MfOperation>().AsNoTracking().AnyAsync(o => !o.Deleted && o.PurchaseRequestId != null && prs.Contains(o.PurchaseRequestId.Value), ct)) return true;
            List<int> orders = await uow.Set<Entities.Model.NasbNiro.Procurement.PurchaseOrder>().AsNoTracking()
                .Where(o => !o.Deleted && ((o.InquiryId != null && prs.Contains(o.Inquiry.PurchaseRequestId)) || o.Lines.Any(l => l.RequestLine != null && prs.Contains(l.RequestLine.PurchaseRequestId)))).Select(o => o.Id).ToListAsync(ct);
            if (orders.Count == 0) return false;
            return await uow.Set<Entities.Model.NasbNiro.Inventory.InventoryDocument>().AsNoTracking()
                .AnyAsync(d => !d.Deleted && ((d.PurchaseOrderId != null && orders.Contains(d.PurchaseOrderId.Value)) || d.Lines.Any(l => l.PurchaseOrderLine != null && orders.Contains(l.PurchaseOrderLine.PurchaseOrderId))), ct);
        }
    }
}
