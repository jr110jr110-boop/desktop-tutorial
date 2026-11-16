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
            services.AddScoped<IAgentStationProbe, QualityProbe>();
            services.AddScoped<IAgentStationProbe, ProductionProbe>();
            services.AddScoped<IAgentStationProbe, PackingProbe>();
            services.AddScoped<IAgentStationProbe, DeliveryProbe>();
            services.AddScoped<IAgentStationProbe, FinanceProbe>();
            services.AddScoped<IAgentStationProbe, EngineeringProbe>();
            services.AddScoped<IAgentStationProbe, SalesProbe>();
            services.AddScoped<IAgentStationProbe, ServiceProbe>();
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
    /// <summary>Station 9: a release note of the contract is released and none is still a draft.</summary>
    internal sealed class QualityProbe : IAgentStationProbe
    {
        public int Station => 9;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            List<int> released = await uow.Set<Entities.Model.NasbNiro.Quality.ReleaseNote>().AsNoTracking().Where(n => !n.Deleted && n.Status == Entities.Model.NasbNiro.Quality.ReleaseNoteStatus.Released && contractIds.Contains(n.ContractId)).Select(n => n.ContractId).Distinct().ToListAsync(ct);
            List<int> drafts = await uow.Set<Entities.Model.NasbNiro.Quality.ReleaseNote>().AsNoTracking().Where(n => !n.Deleted && n.Status == Entities.Model.NasbNiro.Quality.ReleaseNoteStatus.Draft && contractIds.Contains(n.ContractId)).Select(n => n.ContractId).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(released, drafts);
        }
    }

    /// <summary>Station 8: the contract has production orders and none is still a draft, released or in progress (completed, closed and cancelled orders are finished).</summary>
    internal sealed class ProductionProbe : IAgentStationProbe
    {
        public int Station => 8;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<Entities.Model.NasbNiro.Production.MfProductionOrder> orders = uow.Set<Entities.Model.NasbNiro.Production.MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && contractIds.Contains(o.ContractId));
            List<int> started = await orders.Where(o => o.Status == Entities.Model.NasbNiro.Production.MfOrderStatus.Completed || o.Status == Entities.Model.NasbNiro.Production.MfOrderStatus.Closed).Select(o => o.ContractId).Distinct().ToListAsync(ct);
            List<int> open = await orders.Where(o => o.Status == Entities.Model.NasbNiro.Production.MfOrderStatus.Draft || o.Status == Entities.Model.NasbNiro.Production.MfOrderStatus.Released || o.Status == Entities.Model.NasbNiro.Production.MfOrderStatus.InProgress)
                .Select(o => o.ContractId).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, open);
        }
    }

    /// <summary>Station 10: the contract has a shipment, every shipment has left (shipped or later) and no work order with a released release note is still without a shipment.</summary>
    internal sealed class PackingProbe : IAgentStationProbe
    {
        public int Station => 10;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<PcShipment> shipments = uow.Set<PcShipment>().AsNoTracking().Where(s => !s.Deleted && contractIds.Contains(s.ContractId));
            List<int> started = await shipments.Where(s => s.Status >= PcShipmentStatus.Shipped).Select(s => s.ContractId).Distinct().ToListAsync(ct);
            List<int> open = await shipments.Where(s => s.Status < PcShipmentStatus.Shipped).Select(s => s.ContractId).Distinct().ToListAsync(ct);
            IQueryable<int> shippedOrders = uow.Set<PcShipment>().AsNoTracking().Where(s => !s.Deleted).Select(s => s.WorkOrderId);
            List<int> unshipped = await uow.Set<Entities.Model.NasbNiro.Quality.ReleaseNote>().AsNoTracking().Where(n => !n.Deleted && n.Status == Entities.Model.NasbNiro.Quality.ReleaseNoteStatus.Released && contractIds.Contains(n.ContractId) && !shippedOrders.Contains(n.WorkOrderId))
                .Select(n => n.ContractId).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, open.Concat(unshipped));
        }
    }

    /// <summary>Station 11: a shipment of the contract reached the customer and none that left is still waiting for the receipt, the handover minutes or the commissioning date.</summary>
    internal sealed class DeliveryProbe : IAgentStationProbe
    {
        public int Station => 11;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<PcShipment> shipments = uow.Set<PcShipment>().AsNoTracking().Where(s => !s.Deleted && contractIds.Contains(s.ContractId));
            List<int> started = await shipments.Where(s => s.Status >= PcShipmentStatus.Delivered).Select(s => s.ContractId).Distinct().ToListAsync(ct);
            List<int> open = await shipments.Where(s => s.Status == PcShipmentStatus.Shipped || s.Status == PcShipmentStatus.Delivered || (s.Status == PcShipmentStatus.HandoverReceived && s.CommissionedAt == null))
                .Select(s => s.ContractId).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, open);
        }
    }

    /// <summary>
    /// Station 13: the contract has a progress statement that was not rejected, or a posted sales invoice, and none of its statements is still on its way (anything before paid, returned included) and no
    /// sales invoice of it is still a draft. The Sepidar packages belong to a period, not to a contract, so they are not part of the question.
    /// </summary>
    internal sealed class FinanceProbe : IAgentStationProbe
    {
        public int Station => 13;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<Entities.Model.NasbNiro.ContractsSystem.PsStatement> statements = uow.Set<Entities.Model.NasbNiro.ContractsSystem.PsStatement>().AsNoTracking()
                .Where(s => !s.Deleted && s.ContractId != null && contractIds.Contains(s.ContractId.Value));
            IQueryable<Entities.Model.NasbNiro.Finance.FnSalesInvoice> invoices = uow.Set<Entities.Model.NasbNiro.Finance.FnSalesInvoice>().AsNoTracking()
                .Where(i => i.ContractId != null && contractIds.Contains(i.ContractId.Value));
            List<int> startedByStatement = await statements.Where(s => s.Status != Entities.Model.NasbNiro.ContractsSystem.PsStatus.Rejected).Select(s => s.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> startedByInvoice = await invoices.Where(i => i.Status == Entities.Model.NasbNiro.Finance.FnDocStatus.Posted).Select(i => i.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> openStatements = await statements.Where(s => s.Status != Entities.Model.NasbNiro.ContractsSystem.PsStatus.Paid && s.Status != Entities.Model.NasbNiro.ContractsSystem.PsStatus.Rejected).Select(s => s.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> openInvoices = await invoices.Where(i => i.Status == Entities.Model.NasbNiro.Finance.FnDocStatus.Draft).Select(i => i.ContractId.Value).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(startedByStatement.Concat(startedByInvoice), openStatements.Concat(openInvoices));
        }
    }

    /// <summary>
    /// Station 5: the contract has a current baseline and every work order of it has one, and no product of its work orders has a BOM in the signature flow (under review or approved, not yet released) or a routing chart
    /// under review or approved and not yet published to the shop floor system.
    /// </summary>
    internal sealed class EngineeringProbe : IAgentStationProbe
    {
        public int Station => 5;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<Entities.Model.NasbNiro.Contracts.WorkOrder> orders = uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => !w.Deleted && contractIds.Contains(w.ContractId));
            IQueryable<int> baselined = uow.Set<Entities.Model.NasbNiro.ProductBank.PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent).Select(b => b.WorkOrderId);
            List<int> started = await orders.Where(w => baselined.Contains(w.Id)).Select(w => w.ContractId).Distinct().ToListAsync(ct);
            List<int> waiting = await orders.Where(w => !baselined.Contains(w.Id)).Select(w => w.ContractId).Distinct().ToListAsync(ct);
            List<int> inFlow = await orders.Where(w => uow.Set<Entities.Model.NasbNiro.ProductBank.PbProduct>().Any(p => !p.Deleted && p.EquipmentId == w.EquipmentId
                    && (uow.Set<Entities.Model.NasbNiro.ProductBank.PbBomHeader>().Any(h => !h.Deleted && h.ProductId == p.Id && (h.Status == Entities.Model.NasbNiro.ProductBank.PbBomStatus.UnderReview || h.Status == Entities.Model.NasbNiro.ProductBank.PbBomStatus.Approved))
                        || uow.Set<Entities.Model.NasbNiro.ProductBank.PbOpcRouting>().Any(r => !r.Deleted && r.ProductId == p.Id
                            && (r.Status == Entities.Model.NasbNiro.ProductBank.PbRoutingStatus.UnderReview || (r.Status == Entities.Model.NasbNiro.ProductBank.PbRoutingStatus.Approved && r.PublishedToMesAt == null))))))
                .Select(w => w.ContractId).Distinct().ToListAsync(ct);
            // ENG-E1: an engineering case that is not yet released to execution keeps the station open
            List<int> openCases = await uow.Set<Entities.Model.NasbNiro.Engineering.EnCase>().AsNoTracking().Where(c => !c.Deleted && c.ContractId != null && contractIds.Contains(c.ContractId.Value)
                    && (c.Status == Entities.Model.NasbNiro.Engineering.EnCaseStatus.Awarded || c.Status == Entities.Model.NasbNiro.Engineering.EnCaseStatus.InEngineering || c.Status == Entities.Model.NasbNiro.Engineering.EnCaseStatus.HandedOver || c.Status == Entities.Model.NasbNiro.Engineering.EnCaseStatus.Accepted))
                .Select(c => c.ContractId.Value).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, waiting.Concat(inFlow).Concat(openCases));
        }
    }

    /// <summary>Station 1: the contract was made by a won proposal and no proposal of the same inquiry is still on its way (draft, approved or submitted).</summary>
    internal sealed class SalesProbe : IAgentStationProbe
    {
        public int Station => 1;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<Entities.Model.NasbNiro.Sales.SlProposal> won = uow.Set<Entities.Model.NasbNiro.Sales.SlProposal>().AsNoTracking()
                .Where(p => !p.Deleted && p.Status == Entities.Model.NasbNiro.Sales.SlProposalStatus.Won && p.ContractId != null && contractIds.Contains(p.ContractId.Value));
            List<int> started = await won.Select(p => p.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> open = await won.Where(p => uow.Set<Entities.Model.NasbNiro.Sales.SlProposal>().Any(q => !q.Deleted && q.InquiryId == p.InquiryId
                    && (q.Status == Entities.Model.NasbNiro.Sales.SlProposalStatus.Draft || q.Status == Entities.Model.NasbNiro.Sales.SlProposalStatus.Approved || q.Status == Entities.Model.NasbNiro.Sales.SlProposalStatus.Submitted)))
                .Select(p => p.ContractId.Value).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, open);
        }
    }

    /// <summary>
    /// Station 12: a service order of the contract was closed, and nothing of the after-sales service is open for the contract: no order that is planned or in progress, no open request of its units, no unit still
    /// waiting for its confirmation.
    /// </summary>
    internal sealed class ServiceProbe : IAgentStationProbe
    {
        public int Station => 12;

        public async Task<HashSet<int>> CompleteContractsAsync(IUnitOfWork uow, ICollection<int> contractIds, CancellationToken ct)
        {
            if (contractIds.Count == 0) return new HashSet<int>();
            IQueryable<Entities.Model.NasbNiro.AfterSales.SvServiceOrder> orders = uow.Set<Entities.Model.NasbNiro.AfterSales.SvServiceOrder>().AsNoTracking().Where(o => !o.Deleted && o.ContractId != null && contractIds.Contains(o.ContractId.Value));
            List<int> started = await orders.Where(o => o.Status == Entities.Model.NasbNiro.AfterSales.SvOrderStatus.Closed).Select(o => o.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> openOrders = await orders.Where(o => o.Status == Entities.Model.NasbNiro.AfterSales.SvOrderStatus.Draft || o.Status == Entities.Model.NasbNiro.AfterSales.SvOrderStatus.Planned || o.Status == Entities.Model.NasbNiro.AfterSales.SvOrderStatus.InProgress)
                .Select(o => o.ContractId.Value).Distinct().ToListAsync(ct);
            IQueryable<Entities.Model.NasbNiro.AfterSales.SvInstalledUnit> units = uow.Set<Entities.Model.NasbNiro.AfterSales.SvInstalledUnit>().AsNoTracking().Where(u => !u.Deleted && u.ContractId != null && contractIds.Contains(u.ContractId.Value));
            List<int> unconfirmed = await units.Where(u => !u.IsConfirmed && !u.Decommissioned).Select(u => u.ContractId.Value).Distinct().ToListAsync(ct);
            List<int> openRequests = await units.Where(u => uow.Set<Entities.Model.NasbNiro.AfterSales.SvServiceRequest>().Any(r => !r.Deleted && r.UnitId == u.Id
                    && (r.Status == Entities.Model.NasbNiro.AfterSales.SvRequestStatus.Open || r.Status == Entities.Model.NasbNiro.AfterSales.SvRequestStatus.Assigned || r.Status == Entities.Model.NasbNiro.AfterSales.SvRequestStatus.InProgress
                        || r.Status == Entities.Model.NasbNiro.AfterSales.SvRequestStatus.WaitingCustomer)))
                .Select(u => u.ContractId.Value).Distinct().ToListAsync(ct);
            return AgentStationProbes.Difference(started, openOrders.Concat(unconfirmed).Concat(openRequests));
        }
    }
}
