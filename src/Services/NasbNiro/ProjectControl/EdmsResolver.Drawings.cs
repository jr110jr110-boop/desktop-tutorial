using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>A drawing (a document linked to a part) with the revision that is in force now.</summary>
    public sealed class PartDrawing
    {
        public int DeliverableId;
        public int RevisionId;
        public string Number;
        public string Title;
        public string RevisionCode;
        public PcDocStatus RevisionStatus;
        public PcDocLinkKind Via;
    }

    public static partial class EdmsResolver
    {
        /// <summary>
        /// Package B: the drawings of a PART (not of an order): the documents linked to the item or to its part number (with the stage when the item has one) that have a revision in force (Released or As-Built).
        /// The one answer that the MES release gate, the characteristic lists, the quality records and the first article inspection use, so they can never disagree about which drawing revision is in force.
        /// A drawing is company-wide once it is released (the same rule as <see cref="ForOrderAsync"/>).
        /// </summary>
        public static async Task<List<PartDrawing>> DrawingsForItemAsync(IUnitOfWork uow, int itemId, int? operationTemplateId, CancellationToken ct)
        {
            var item = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == itemId).Select(i => new { i.PartNumber, i.StageCode2 }).FirstOrDefaultAsync(ct);
            if (item == null) return new List<PartDrawing>();
            var keys = new EdmsKeys { ItemId = itemId, PartNumber = string.IsNullOrWhiteSpace(item.PartNumber) ? null : item.PartNumber.Trim(), StageCode2 = item.StageCode2 };
            List<PcDocumentLink> links = await MatchingLinksAsync(uow, keys, operationTemplateId, operationTemplateId.HasValue, ct);
            var found = new Dictionary<int, EdmsFoundDocument>();
            foreach (PcDocumentLink l in links.Where(l => l.UnlinkReason == null && l.DeliverableId.HasValue && (l.Kind == PcDocLinkKind.Item || l.Kind == PcDocLinkKind.BomLine || l.Kind == PcDocLinkKind.PartNumber)))
                if (!found.ContainsKey(l.DeliverableId.Value))
                    found[l.DeliverableId.Value] = new EdmsFoundDocument { Via = l.Kind, ViaText = ViaText(l), Deliverable = new PcEngineeringDeliverable { Id = l.DeliverableId.Value } };
            if (found.Count == 0) return new List<PartDrawing>();
            await FillAsync(uow, found.Values.ToList(), ct);
            return found.Values.Where(f => f.Number != null && f.Released != null)
                .OrderBy(f => f.Number, StringComparer.Ordinal)
                .Select(f => new PartDrawing { DeliverableId = f.Deliverable.Id, RevisionId = f.Released.Id, Number = f.Number, Title = f.Title, RevisionCode = f.Released.Code, RevisionStatus = f.Released.Status, Via = f.Via })
                .ToList();
        }

        /// <summary>True when the part has at least one released drawing.</summary>
        public static async Task<bool> HasReleasedDrawingAsync(IUnitOfWork uow, int itemId, CancellationToken ct) => (await DrawingsForItemAsync(uow, itemId, null, ct)).Count > 0;
    }
}
