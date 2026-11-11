using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro
{
    /// <summary>Flat projection of a BOM line or assembly of a product, used by the predecessor rules.</summary>
    internal sealed class PredecessorNode
    {
        public int Id { get; set; }
        public int? PredecessorId { get; set; }
        public bool Deleted { get; set; }
        public int EquipmentId { get; set; }
        public string Title { get; set; }
    }

    /// <summary>
    /// The one rule set for choosing a predecessor of a BOM line or an assembly of a product:
    /// it exists, is not deleted, belongs to the same product, and does not close a loop.
    /// </summary>
    internal static class PredecessorValidator
    {
        /// <param name="label">Persian noun of the item kind, e.g. BOM or مونتاژ.</param>
        /// <param name="nodes">All rows of the kind (unfiltered).</param>
        /// <param name="selfId">The item that receives the predecessor, null while it is not saved yet.</param>
        /// <returns>A Persian refusal message, or null when the predecessor is acceptable.</returns>
        internal static async Task<string> ValidateAsync(
            string label, IQueryable<PredecessorNode> nodes, int? selfId, int equipmentId, int predecessorId, CancellationToken cancellationToken)
        {
            if (selfId.HasValue && selfId.Value == predecessorId)
            {
                return "یک مورد نمی‌تواند پیش‌نیاز خودش باشد.";
            }

            PredecessorNode predecessor = await nodes.Where(n => n.Id == predecessorId).FirstOrDefaultAsync(cancellationToken);
            if (predecessor == null)
            {
                return label + " پیش‌نیاز با شناسهٔ " + predecessorId + " وجود ندارد.";
            }

            if (predecessor.Deleted)
            {
                return label + " پیش‌نیاز «" + predecessor.Title + "» حذف شده است.";
            }

            if (predecessor.EquipmentId != equipmentId)
            {
                return label + " پیش‌نیاز «" + predecessor.Title + "» متعلق به محصول دیگری است.";
            }

            Dictionary<int, int?> chain = await nodes
                .Where(n => n.EquipmentId == equipmentId && !n.Deleted)
                .ToDictionaryAsync(n => n.Id, n => n.PredecessorId, cancellationToken);

            if (PredecessorOrdering.WouldCloseLoop(selfId, predecessorId, id => chain.TryGetValue(id, out int? p) ? p : null))
            {
                return "انتخاب این پیش‌نیاز حلقه می‌سازد؛ پیش‌نیازها نباید دوری شوند.";
            }

            return null;
        }
    }
}
