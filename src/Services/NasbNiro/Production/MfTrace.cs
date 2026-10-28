using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Production
{
    /// <summary>
    /// R1p D42 and D33: what identifies the pieces an order makes, for the records quality and the stock keep about them. A lot-tracked item is made in one lot named after the order (made at the release);
    /// a serial-tracked item carries the serial numbers planned on the order. An untracked item has neither.
    /// </summary>
    internal static class MfTrace
    {
        public static async Task<(int? LotId, List<string> Serials)> OfOrderAsync(IUnitOfWork uow, MfProductionOrder order, CancellationToken ct)
        {
            if (!order.ItemId.HasValue) return (null, new List<string>());
            int itemId = order.ItemId.Value;
            ItemTrackingMode? mode = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == itemId).Select(i => (ItemTrackingMode?)i.TrackingMode).FirstOrDefaultAsync(ct);
            if (mode == ItemTrackingMode.Lot)
            {
                int? lotId = await uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && l.ItemId == itemId && l.LotNumber == order.Number).Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
                return (lotId, new List<string>());
            }
            if (mode == ItemTrackingMode.Serial) return (null, MfSerialRules.Planned(order.SerialsPlanned));
            return (null, new List<string>());
        }
    }
}
