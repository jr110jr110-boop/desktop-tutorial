using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.ProductBank;

namespace Services.NasbNiro.ProductBank
{
    /// <summary>
    /// PB2: the quantity of every line of a project BOM from the tree. A line's quantity is per unit of its parent assembly, so the quantity for the work order is
    /// the line's quantity x the quantities of all its ancestors x the work order count. A removed line, and everything below a removed assembly, counts zero.
    /// An assembly may carry a scrap factor: its parts are multiplied by (1 + factor) as well.
    /// </summary>
    public static class PbBomTree
    {
        public static Dictionary<int, decimal> Totals(IEnumerable<PbProjectBomLine> lines, int count, Func<PbProjectBomLine, decimal> assemblyScrap)
        {
            var byId = lines.Where(l => l.Id > 0).ToDictionary(l => l.Id);
            var memo = new Dictionary<int, decimal?>();
            decimal c = Math.Max(1, count);

            // the multiplier a part sees from its parent chain; null = the chain is cut by a removed assembly
            decimal? Chain(PbProjectBomLine l, int depth)
            {
                if (l == null) return 1m;
                if (depth > 30 || l.IsRemoved) return null;
                if (memo.TryGetValue(l.Id, out decimal? known)) return known;
                decimal? up = l.ParentLineId.HasValue && byId.TryGetValue(l.ParentLineId.Value, out PbProjectBomLine p) ? Chain(p, depth + 1) : 1m;
                decimal? mine = up == null ? (decimal?)null : up.Value * l.Qty * (1m + Math.Max(0m, assemblyScrap == null ? 0m : assemblyScrap(l)));
                memo[l.Id] = mine;
                return mine;
            }

            var result = new Dictionary<int, decimal>();
            foreach (PbProjectBomLine l in byId.Values)
            {
                if (l.IsRemoved) { result[l.Id] = 0m; continue; }
                decimal? up = l.ParentLineId.HasValue && byId.TryGetValue(l.ParentLineId.Value, out PbProjectBomLine p) ? Chain(p, 0) : 1m;
                result[l.Id] = up == null ? 0m : Math.Round(up.Value * l.Qty * c, 4, MidpointRounding.AwayFromZero);
            }
            return result;
        }
    }
}
