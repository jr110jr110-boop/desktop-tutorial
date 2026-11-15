using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Production;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// Priority 4d: one resource truth with production. A Machine resource of the pool that is linked to a production work centre (and counted in hours) does not own a capacity or a calendar: both are read from the work centre
    /// every time they are needed. This class is the one shared query service of the two views that show it: the resource histogram of project control (it adds the production layer «بار تولید» to a linked resource) and the
    /// finite-capacity loading view of production (it adds the project layer «بار پروژه‌ها» to a work centre). No table is duplicated: the project layer is computed from the approved resource plans, the production layer
    /// from the production scheduler, both on the fly.
    /// </summary>
    internal static class PcWorkCentreLink
    {
        /// <summary>The capacity of a linked machine in units that work at once: parallel resources x efficiency x utilisation (percent as fractions). Times the hours of a day it is exactly the available hours per day of the loading view before downtime.</summary>
        public static decimal CapacityOf(MfWorkCenter wc) => Math.Max(1, wc.ParallelResources) * (wc.EfficiencyPercent / 100m) * (wc.UtilisationPercent / 100m);

        /// <summary>The hours of a working day of the work centre: shifts x hours per shift of one resource.</summary>
        public static decimal DayHoursOf(MfWorkCenter wc) => Math.Max(1, wc.ShiftsPerDay) * wc.HoursPerShift;

        /// <summary>The capacity as it is stored in the resource row (three decimals, as the column is); the read-through value is the exact one.</summary>
        public static decimal StoredCapacityOf(MfWorkCenter wc)
        {
            decimal c = Math.Round(CapacityOf(wc), 3, MidpointRounding.AwayFromZero);
            return c > 0m ? c : 0.001m;
        }

        /// <summary>The one rule of a read-through: a Machine resource with a work centre AND counted in hours. Any other machine keeps its own capacity and calendar.</summary>
        public static bool IsLinked(PcResource r) => r.Kind == PcResourceKind.Machine && r.WorkCenterId.HasValue && r.Unit == PcResourceUnit.Hour;

        internal sealed class Link
        {
            public int ResourceId { get; set; }
            public int WorkCenterId { get; set; }
            public PcResourceUnit Unit { get; set; }
        }

        /// <summary>Gives every linked machine resource of the list the capacity, the calendar and the day length of its work centre. A resource whose work centre is gone keeps its own values.</summary>
        public static async Task ApplyAsync(IUnitOfWork uow, IEnumerable<LoadResource> resources, CancellationToken ct)
        {
            List<LoadResource> linked = resources.Where(r => r.WorkCenterId.HasValue && r.Unit == PcResourceUnit.Hour).ToList();
            if (linked.Count == 0) return;
            int[] ids = linked.Select(r => r.WorkCenterId.Value).Distinct().OrderBy(i => i).ToArray();
            Dictionary<int, MfWorkCenter> centres = await uow.Set<MfWorkCenter>().AsNoTracking().Where(w => ids.Contains(w.Id) && !w.Deleted).ToDictionaryAsync(w => w.Id, ct);
            foreach (LoadResource r in linked)
            {
                if (!centres.TryGetValue(r.WorkCenterId.Value, out MfWorkCenter wc)) { r.WorkCenterId = null; continue; }
                r.Capacity = CapacityOf(wc); r.CalendarId = wc.CalendarId; r.FixedHours = DayHoursOf(wc);
            }
        }

        /// <summary>The active machine resources that are linked to a live work centre and counted in hours.</summary>
        public static async Task<List<Link>> LinksAsync(IUnitOfWork uow, CancellationToken ct) =>
            await uow.Set<PcResource>().AsNoTracking().Where(r => !r.Deleted && r.IsActive && r.Kind == PcResourceKind.Machine && r.Unit == PcResourceUnit.Hour && r.WorkCenterId != null
                    && uow.Set<MfWorkCenter>().Any(w => w.Id == r.WorkCenterId && !w.Deleted))
                .OrderBy(r => r.Id).Select(r => new Link { ResourceId = r.Id, WorkCenterId = r.WorkCenterId.Value, Unit = r.Unit }).ToListAsync(ct);

        /// <summary>
        /// The production orders that are tied (MfProductionOrder.ActivityId) to a project activity that also carries an assignment of the linked machine resource of the work centre are the SAME work seen from the two sides:
        /// their demand at that work centre is taken out of the production layer so it is counted once (as project load). Returns the hours to take off, per work centre and day.
        /// </summary>
        internal static Dictionary<(int WorkCenterId, DateTime Day), decimal> TiedDemand(MfScheduleData data, IEnumerable<(int ActivityId, int WorkCenterId)> tied)
        {
            var result = new Dictionary<(int, DateTime), decimal>();
            HashSet<(int, int)> set = tied.ToHashSet();
            if (set.Count == 0) return result;
            foreach (MfOrderPlan plan in data.Orders)
            {
                if (!plan.Order.ActivityId.HasValue) continue;
                foreach (int centre in set.Where(t => t.Item1 == plan.Order.ActivityId.Value).Select(t => t.Item2).Distinct().OrderBy(i => i))
                    foreach (KeyValuePair<(int, DateTime), decimal> kv in MfScheduler.OrderDemandAt(data, plan, centre))
                        result[kv.Key] = (result.TryGetValue(kv.Key, out decimal v) ? v : 0m) + kv.Value;
            }
            return result;
        }

        /// <summary>
        /// The production layer: the hours the open work orders need on the work centre of each linked resource of the model and day (the same demand that the loading view of production shows), added as cells of their own.
        /// Nothing is read from production when the model holds no linked resource.
        /// </summary>
        public static async Task AddProductionCellsAsync(IUnitOfWork uow, LoadModel model, DateTime today, DateTime from, DateTime to, CancellationToken ct)
        {
            Dictionary<int, List<int>> byCentre = model.Resources.Values.Where(r => r.WorkCenterId.HasValue && r.Unit == PcResourceUnit.Hour).GroupBy(r => r.WorkCenterId.Value).ToDictionary(g => g.Key, g => g.Select(r => r.Id).OrderBy(i => i).ToList());
            if (byCentre.Count == 0) return;
            MfScheduleData data = await MfScheduler.ComputeAsync(uow, today, ct, false);
            Dictionary<int, int> centreOf = model.Resources.Values.Where(r => r.WorkCenterId.HasValue && r.Unit == PcResourceUnit.Hour).ToDictionary(r => r.Id, r => r.WorkCenterId.Value);
            Dictionary<(int WorkCenterId, DateTime Day), decimal> same = TiedDemand(data, model.Rows.Where(r => centreOf.ContainsKey(r.ResourceId)).Select(r => (r.ActivityId, centreOf[r.ResourceId])));
            foreach (KeyValuePair<(int, DateTime), decimal> kv in data.Demand.OrderBy(k => k.Key.Item1).ThenBy(k => k.Key.Item2))
            {
                decimal hours = kv.Value - (same.TryGetValue(kv.Key, out decimal tiedHours) ? tiedHours : 0m);
                if (hours <= 0m || kv.Key.Item2 < from.Date || kv.Key.Item2 > to.Date || !byCentre.TryGetValue(kv.Key.Item1, out List<int> resourceIds)) continue;
                // two resources on one work centre would double the layer: the lowest id carries it
                model.Cells.Add(new LoadCell { ResourceId = resourceIds[0], Date = kv.Key.Item2.Date, ActivityId = 0, Value = hours, IsProduction = true });
            }
        }

        internal sealed class ProjectLayer
        {
            public Dictionary<(int WorkCenterId, DateTime Day), decimal> Hours { get; } = new Dictionary<(int, DateTime), decimal>();

            /// <summary>The project activities that carry an assignment of a linked machine, with the work centre of that machine.</summary>
            public HashSet<(int ActivityId, int WorkCenterId)> Tied { get; } = new HashSet<(int, int)>();
        }

        /// <summary>
        /// The project layer: the hours per work centre and day that the approved resource plans of the live projects put on the linked machine resources of the work centre. Empty when no resource is linked.
        /// </summary>
        public static async Task<ProjectLayer> ProjectLayerAsync(IUnitOfWork uow, DateTime today, DateTime from, DateTime to, List<Link> links, CancellationToken ct)
        {
            var layer = new ProjectLayer();
            if (links.Count == 0) return layer;
            int[] ids = links.Select(l => l.ResourceId).ToArray();
            LoadModel model = await LoadModel.LoadAsync(uow, today, null, false, from, to, ct, false, ids);
            Dictionary<int, int> centreOf = links.ToDictionary(l => l.ResourceId, l => l.WorkCenterId);
            foreach (LoadCell c in model.Cells)
            {
                if (c.IsProduction || !centreOf.TryGetValue(c.ResourceId, out int wc)) continue;
                var key = (wc, c.Date.Date);
                layer.Hours[key] = (layer.Hours.TryGetValue(key, out decimal v) ? v : 0m) + c.Value;
            }
            foreach (LoadRow r in model.Rows) if (centreOf.TryGetValue(r.ResourceId, out int centre)) layer.Tied.Add((r.ActivityId, centre));
            return layer;
        }
    }
}
