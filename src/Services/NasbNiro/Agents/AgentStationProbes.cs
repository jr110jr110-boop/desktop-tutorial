using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Services.NasbNiro.Agents
{
    /// <summary>
    /// The per-station "complete" question of the board: which of the given contracts have finished the station, computed from the state of the documents only (no entity of its own). A station is complete when
    /// it was started for the contract and nothing of it is still open. The board shows such a cell as done instead of idle.
    /// </summary>
    public interface IAgentStationProbe
    {
        int Station { get; }

        Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct);
    }

    public static class AgentStationProbes
    {
        public static IServiceCollection AddAgentStationProbes(this IServiceCollection services)
        {
            services.AddScoped<IAgentStationProbe, ProcurementProbe>();
            services.AddScoped<IAgentStationProbe, WarehouseProbe>();
            services.AddScoped<IAgentStationProbe, ContractReviewProbe>();
            services.AddScoped<IAgentStationProbe, InitiationProbe>();
            services.AddScoped<IAgentStationProbe, PlanningProbe>();
            return services;
        }

        /// <summary>The contracts that are in <paramref name="started"/> and not in <paramref name="open"/>.</summary>
        public static HashSet<int> Difference(IEnumerable<int> started, IEnumerable<int> open)
        {
            var result = new HashSet<int>(started);
            result.ExceptWith(open);
            return result;
        }
    }

    /// <summary>Station 6: the contract has purchase requests, none is still a draft, submitted, approved (without its order) or returned without a revision, and no order is still open.</summary>
    internal sealed class ProcurementProbe : IAgentStationProbe
    {
        public int Station => 6;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            List<int> started = await uow.Set<PurchaseRequest>().AsNoTracking().Where(r => !r.Deleted && r.ContractId != null && contractIds.Contains(r.ContractId.Value) && r.Status != PurchaseRequestStatus.Rejected)
                .Select(r => r.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> openRequests = await uow.Set<PurchaseRequest>().AsNoTracking().Where(r => !r.Deleted && r.ContractId != null && contractIds.Contains(r.ContractId.Value)
                    && (r.Status == PurchaseRequestStatus.Draft || r.Status == PurchaseRequestStatus.Submitted || r.Status == PurchaseRequestStatus.Approved || (r.Status == PurchaseRequestStatus.Returned && r.SupersededByRequestId == null)))
                .Select(r => r.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> openOrders = await uow.Set<PurchaseOrder>().AsNoTracking().Where(o => !o.Deleted && o.ContractId != null && contractIds.Contains(o.ContractId.Value)
                    && (o.Status == PurchaseOrderStatus.Draft || o.Status == PurchaseOrderStatus.Approved || o.Status == PurchaseOrderStatus.Released || o.Status == PurchaseOrderStatus.PartlyReceived))
                .Select(o => o.ContractId.Value).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, openRequests.Concat(openOrders));
        }
    }

    /// <summary>Station 7: the contract's work orders have material requests, and every one is closed, rejected or issued in full, with no issue or receipt still a draft.</summary>
    internal sealed class WarehouseProbe : IAgentStationProbe
    {
        public int Station => 7;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<MaterialRequest> requests = uow.Set<MaterialRequest>().AsNoTracking().Where(m => !m.Deleted && m.WorkOrderId != null && contractIds.Contains(m.WorkOrder.ContractId));
            List<int> started = await requests.Where(m => m.Status != MaterialRequestStatus.Rejected).Select(m => m.WorkOrder.ContractId).Distinct().ToListAsync(ct);
            List<int> open = await requests.Where(m => m.Status == MaterialRequestStatus.Draft || m.Status == MaterialRequestStatus.Submitted
                                                       || (m.Status == MaterialRequestStatus.Approved && m.Lines.Any(l => l.IssuedQuantity < l.Quantity)))
                .Select(m => m.WorkOrder.ContractId).Distinct().ToListAsync(ct);
            List<int> drafts = await uow.Set<InventoryDocument>().AsNoTracking().Where(d => !d.Deleted && d.Status == InventoryDocumentStatus.Draft && d.MaterialRequestId != null
                                                                                          && contractIds.Contains(d.MaterialRequest.WorkOrder.ContractId))
                .Select(d => d.MaterialRequest.WorkOrder.ContractId).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, open.Concat(drafts));
        }
    }

    /// <summary>Station 2: the contract review of the contract is accepted (both signatures) and none is still open.</summary>
    internal sealed class ContractReviewProbe : IAgentStationProbe
    {
        public int Station => 2;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<PcContractReview> reviews = uow.Set<PcContractReview>().AsNoTracking().Where(r => !r.Deleted && r.Stage == PcReviewStage.Contract && r.ContractId != null && contractIds.Contains(r.ContractId.Value));
            List<int> started = await reviews.Where(r => r.Decision == PcReviewDecision.Accept).Select(r => r.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> open = await reviews.Where(r => r.Decision == PcReviewDecision.Pending || r.Decision == PcReviewDecision.ReviewRequired).Select(r => r.ContractId.Value).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, open);
        }
    }

    /// <summary>Station 3: the initiation record exists and every step up to the BOM request is done (the WBS and the baseline belong to station 4).</summary>
    internal sealed class InitiationProbe : IAgentStationProbe
    {
        public int Station => 3;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            List<int> started = await uow.Set<PcProjectInitiation>().AsNoTracking().Where(i => !i.Deleted && contractIds.Contains(i.ContractId)).Select(i => i.ContractId).Distinct().ToListAsync(ct);
            List<int> open = await uow.Set<PcInitiationStepRecord>().AsNoTracking().Where(s => !s.Initiation.Deleted && contractIds.Contains(s.Initiation.ContractId) && s.DoneAt == null && s.Step <= PcInitiationStep.BomRequested)
                .Select(s => s.Initiation.ContractId).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, open);
        }
    }

    /// <summary>Station 4: an approved baseline is in force for the contract and no baseline or WBS node is waiting for a decision.</summary>
    internal sealed class PlanningProbe : IAgentStationProbe
    {
        public int Station => 4;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            List<int> started = await uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.CurrentBaselineId != null && contractIds.Contains(t.ContractId)).Select(t => t.ContractId).ToListAsync(ct);
            return new HashSet<int>(started);
        }
    }
}
