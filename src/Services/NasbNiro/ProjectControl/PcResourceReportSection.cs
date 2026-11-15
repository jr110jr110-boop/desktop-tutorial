using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// Priority 4e: the «منابع» section of the weekly report of a project: the over-allocations of the resources that the project's plan uses (open, accepted, lapsed), the plans that wait for approval, the resources of the plan that have no approved
    /// rate, and the five resources the project loads most in the next four weeks (a quantity in the unit of the resource, never an amount: nobody's pay can be derived from it).
    /// </summary>
    internal static class PcResourceReportSection
    {
        public const int TopCount = 5;
        public const int WeeksAhead = 4;

        /// <summary>The count of the over-allocations of the given resources by status (one grouped query).</summary>
        internal static IQueryable<OverloadCount> OverloadCounts(IUnitOfWork uow, int[] resourceIds) =>
            uow.Set<PcResourceOverload>().AsNoTracking().Where(o => !o.Deleted && resourceIds.Contains(o.ResourceId)).GroupBy(o => o.Status).Select(g => new OverloadCount { Status = g.Key, Count = g.Count() });

        internal sealed class OverloadCount
        {
            public PcOverloadStatus Status { get; set; }
            public int Count { get; set; }
        }

        public static async Task<PcWeeklyResourcesDto> BuildAsync(IUnitOfWork uow, int contractId, DateTime today, CancellationToken ct)
        {
            var section = new PcWeeklyResourcesDto();
            section.PlansWaitingApproval = await uow.Set<PcResourcePlan>().AsNoTracking().CountAsync(p => !p.Deleted && p.ContractId == contractId && p.Status == PcResourcePlanStatus.Submitted, ct);

            // the resources of the project's plan: the open plan when there is one, else the newest approved
            int? planId = await uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted && p.ContractId == contractId && p.Status != PcResourcePlanStatus.Approved).OrderByDescending(p => p.Version).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct)
                ?? await uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted && p.ContractId == contractId && p.Status == PcResourcePlanStatus.Approved).OrderByDescending(p => p.Version).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
            int[] resourceIds = Array.Empty<int>();
            if (planId.HasValue) resourceIds = (await uow.Set<PcActivityResource>().AsNoTracking().Where(x => x.PlanId == planId.Value && !x.Resource.Deleted).Select(x => x.ResourceId).Distinct().OrderBy(i => i).ToListAsync(ct)).ToArray();
            if (resourceIds.Length > 0)
            {
                List<OverloadCount> statuses = await OverloadCounts(uow, resourceIds).ToListAsync(ct);
                section.OpenOverloads = statuses.Where(s => s.Status == PcOverloadStatus.Open).Sum(s => s.Count);
                section.AcceptedOverloads = statuses.Where(s => s.Status == PcOverloadStatus.Accepted).Sum(s => s.Count);
                section.LapsedOverloads = statuses.Where(s => s.Status == PcOverloadStatus.Lapsed).Sum(s => s.Count);
                Dictionary<int, decimal?> rates = await PcResourceData.EffectiveRatesAsync(uow, resourceIds, today, ct);
                section.ResourcesWithoutRate = resourceIds.Count(id => !rates.TryGetValue(id, out decimal? r) || !r.HasValue);

                LoadModel model = await LoadModel.LoadAsync(uow, today, null, false, today, today.AddDays(7 * WeeksAhead - 1), ct, false, resourceIds);
                var loads = model.Cells.Where(c => !c.IsProduction && model.Activities.TryGetValue(c.ActivityId, out LoadActivity a) && a.ContractId == contractId)
                    .GroupBy(c => c.ResourceId).Select(g => new { Id = g.Key, Load = g.Sum(c => c.Value) }).Where(x => x.Load > 0m).ToList();
                section.Top = loads.Where(x => model.Resources.ContainsKey(x.Id)).OrderByDescending(x => x.Load).ThenBy(x => model.Resources[x.Id].Code, StringComparer.Ordinal).Take(TopCount)
                    .Select(x => new PcWeeklyResourceTopDto { ResourceId = x.Id, Code = model.Resources[x.Id].Code, Title = model.Resources[x.Id].Title, Unit = model.Resources[x.Id].Unit, Load = x.Load }).ToList();
            }
            return section;
        }
    }
}
