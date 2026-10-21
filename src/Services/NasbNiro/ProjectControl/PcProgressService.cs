using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public interface IPcProgressService
    {
        /// <summary>Overall physical percent of a contract (or of one work order), recomputed from the module data. Read only.</summary>
        Task<decimal> PercentAsync(int contractId, int? workOrderId, CancellationToken cancellationToken);

        /// <summary>Recomputes every activity (percent, status, actual dates) on tracked entities. The caller saves.</summary>
        Task<List<PcActivity>> ApplyAsync(int contractId, CancellationToken cancellationToken);

        Task<ResponseDto<PcProgressDto>> GetProgressAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcManualProgressDto>>> GetManualAsync(PcManualQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcManualProgressDto>> ProposeManualAsync(PcManualProgressSaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcManualProgressDto>> DecideManualAsync(PcManualDecisionDto dto, CancellationToken cancellationToken);

        Task<ResponseDto<PcEvmDto>> GetEvmAsync(int contractId, DateTime? asOf, CancellationToken cancellationToken);
        Task<PcEvmDto> ComputeEvmAsync(int contractId, DateTime asOf, CancellationToken cancellationToken);
        Task<ResponseDto<List<PcCashFlowPointDto>>> GetCashFlowAsync(int contractId, CancellationToken cancellationToken);
        Task<ResponseDto<PcCostEntryDto>> AddCostAsync(PcCostEntrySaveDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcCostEntryDto>>> GetCostsAsync(PcContractQuery query, CancellationToken cancellationToken);

        Task<ResponseDto<PcSnapshotDto>> TakeSnapshotAsync(PcSnapshotRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcSnapshotRunResultDto>> RunDueSnapshotsAsync(PcSnapshotRunDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcSnapshotDto>>> GetSnapshotsAsync(PcSnapshotQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcSnapshotDto>> GetSnapshotAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PcDailyReportDto>> GetDailyAsync(PcDailyQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcDailyReportDto>> SaveDailyAsync(PcDailyReportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcDailyReportDto>> ConfirmDailyAsync(int id, CancellationToken cancellationToken);
    }

    /// <summary>Progress engine, earned value, weekly snapshots and daily reports (WI-PL-009 sections 19-29).</summary>
    public class PcProgressService : IPcProgressService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public PcProgressService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        private int Me => _user.UserId;
        private static string Clean(string s) => PcText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => PcText.Bad<T>(m);

        // ============================================================================ the progress engine

        private sealed class Calc
        {
            public List<PcActivity> Activities = new List<PcActivity>();
            public Dictionary<int, decimal> Percent = new Dictionary<int, decimal>();
            public Dictionary<int, DateTime?> StartDate = new Dictionary<int, DateTime?>();
            public Dictionary<int, DateTime?> FinishDate = new Dictionary<int, DateTime?>();
            public Dictionary<int, decimal> Weight = new Dictionary<int, decimal>();
        }

        /// <summary>Percent of one purchase order line from receipts, QC inspections and NCRs (WI-PL-009 sections 19 and 27).</summary>
        private async Task<Dictionary<int, (decimal Percent, DateTime? First, DateTime? Last)>> SupplyAsync(List<int> orderLineIds, Dictionary<string, decimal> settings, CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, (decimal, DateTime?, DateTime?)>();
            if (orderLineIds.Count == 0) return result;
            decimal pAccepted = settings[PcSettingKeys.SupplyAccepted], pTemp = settings[PcSettingKeys.SupplyTemporary], pRework = settings[PcSettingKeys.SupplyRework], pReject = settings[PcSettingKeys.SupplyReject];
            var quantities = await _uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => orderLineIds.Contains(l.Id)).Select(l => new { l.Id, l.Quantity }).ToListAsync(cancellationToken);
            var receipts = await _uow.Set<InventoryDocumentLine>().AsNoTracking()
                .Where(l => l.PurchaseOrderLineId.HasValue && orderLineIds.Contains(l.PurchaseOrderLineId.Value) && l.Document.Status == InventoryDocumentStatus.Confirmed && !l.Document.Deleted &&
                            (l.Document.Type == InventoryDocumentType.PurchaseReceipt || l.Document.Type == InventoryDocumentType.SupplierReturn))
                .Select(l => new { l.Id, Po = l.PurchaseOrderLineId.Value, l.Quantity, l.Document.Type, Date = l.Document.DocumentDate }).ToListAsync(cancellationToken);
            List<int> receiptIds = receipts.Where(r => r.Type == InventoryDocumentType.PurchaseReceipt).Select(r => r.Id).ToList();
            var inspections = await _uow.Set<QcInspection>().AsNoTracking().Where(q => !q.Deleted && receiptIds.Contains(q.ReceiptLineId))
                .Select(q => new { q.Id, q.ReceiptLineId, q.Status, q.AcceptedQuantity, q.InspectedAt }).ToListAsync(cancellationToken);
            List<int> inspectionIds = inspections.Select(i => i.Id).ToList();
            var ncrs = await _uow.Set<NonConformance>().AsNoTracking().Where(n => !n.Deleted && n.InspectionId.HasValue && inspectionIds.Contains(n.InspectionId.Value) && n.Status != NcrStatus.Cancelled)
                .Select(n => new { InspectionId = n.InspectionId.Value, n.Quantity, n.Status, n.Disposition }).ToListAsync(cancellationToken);
            var services = await _uow.Set<ServiceAcceptance>().AsNoTracking().Where(a => !a.Deleted && orderLineIds.Contains(a.PurchaseOrderLineId))
                .Select(a => new { a.PurchaseOrderLineId, a.Quantity, a.AcceptanceDate }).ToListAsync(cancellationToken);

            foreach (var line in quantities)
            {
                decimal accepted = 0m, rework = 0m, temp = 0m, rejected = 0m, returned = 0m;
                DateTime? first = null, last = null;
                foreach (var r in receipts.Where(x => x.Po == line.Id))
                {
                    if (r.Type == InventoryDocumentType.SupplierReturn) { returned += r.Quantity; continue; }
                    if (!first.HasValue || r.Date.Date < first.Value) first = r.Date.Date;
                    var insp = inspections.FirstOrDefault(i => i.ReceiptLineId == r.Id);
                    if (insp == null) { accepted += r.Quantity; if (!last.HasValue || r.Date.Date > last.Value) last = r.Date.Date; continue; }
                    if (insp.Status != InspectionStatus.Done) { temp += r.Quantity; continue; }
                    decimal acc = insp.AcceptedQuantity;
                    decimal lineRework = 0m, lineTemp = 0m;
                    foreach (var n in ncrs.Where(x => x.InspectionId == insp.Id))
                    {
                        bool closed = n.Status == NcrStatus.Executed || n.Status == NcrStatus.Closed;
                        bool fix = n.Disposition == NcrDisposition.Rework || n.Disposition == NcrDisposition.Repair;
                        if (closed && (fix || n.Disposition == NcrDisposition.Concession)) acc += n.Quantity;
                        else if (!closed && fix) lineRework += n.Quantity;
                        else if (!closed && (n.Disposition == NcrDisposition.None || n.Disposition == NcrDisposition.Concession)) lineTemp += n.Quantity;
                        else rejected += n.Quantity;
                    }
                    accepted += Math.Min(acc, r.Quantity); rework += lineRework; temp += lineTemp;
                    DateTime d = (insp.InspectedAt?.Date ?? r.Date.Date);
                    if (acc > 0 && (!last.HasValue || d > last.Value)) last = d;
                }
                foreach (var sv in services.Where(x => x.PurchaseOrderLineId == line.Id)) { accepted += sv.Quantity; if (!first.HasValue || sv.AcceptanceDate.Date < first.Value) first = sv.AcceptanceDate.Date; if (!last.HasValue || sv.AcceptanceDate.Date > last.Value) last = sv.AcceptanceDate.Date; }
                decimal q = line.Quantity <= 0 ? 1m : line.Quantity;
                // received quantity that no inspection decided yet stays at the temporary-receipt step; returns reduce it
                decimal received = receipts.Where(x => x.Po == line.Id && x.Type == InventoryDocumentType.PurchaseReceipt).Sum(x => x.Quantity);
                if (returned > 0) temp = Math.Max(0m, temp - returned);
                decimal points = Math.Min(accepted, q) * pAccepted + Math.Min(rework, q) * pRework + Math.Min(temp, q) * pTemp + Math.Min(rejected, q) * pReject;
                decimal percent = Math.Min(100m, Math.Max(0m, points / q));
                if (received == 0m && services.All(x => x.PurchaseOrderLineId != line.Id)) percent = 0m;
                result[line.Id] = (PcText.Round4(percent), first, percent >= pAccepted ? last : null);
            }
            return result;
        }

        private async Task<Calc> ComputeAsync(int contractId, bool track, DateTime asOf, CancellationToken cancellationToken)
        {
            IQueryable<PcActivity> aq = _uow.Set<PcActivity>().Where(a => a.ContractId == contractId && !a.Deleted);
            if (!track) aq = aq.AsNoTracking();
            var calc = new Calc { Activities = await aq.OrderBy(a => a.Sort).ThenBy(a => a.Id).ToListAsync(cancellationToken) };
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);

            // manual progress: the latest approved proposal
            List<int> manualIds = calc.Activities.Where(a => a.ProgressMethod == PcProgressMethod.Manual || a.ProgressMethod == PcProgressMethod.Contractor || a.ProgressMethod == PcProgressMethod.Quantity).Select(a => a.Id).ToList();
            var manualRows = await _uow.Set<PcManualProgress>().AsNoTracking().Where(m => manualIds.Contains(m.ActivityId) && m.State == 1).OrderBy(m => m.DecidedAt).ThenBy(m => m.Id)
                .Select(m => new { m.ActivityId, m.Percent, m.DecidedAt }).ToListAsync(cancellationToken);

            // engineering: the state of the register lines
            List<int> engOrders = calc.Activities.Where(a => a.ProgressMethod == PcProgressMethod.EngineeringIssue && a.WorkOrderId.HasValue).Select(a => a.WorkOrderId.Value).Distinct().ToList();
            var deliverables = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => !d.Deleted && d.Required && d.WorkOrderId != null && engOrders.Contains(d.WorkOrderId.Value))
                .Select(d => new { WorkOrderId = d.WorkOrderId.Value, d.ClientSubmittal, d.IssueState, d.ApprovedAt, d.ClientResponseDate, d.LastTransmittalDate }).ToListAsync(cancellationToken);

            // supply and services
            List<int> poLineIds = calc.Activities.Where(a => (a.ProgressMethod == PcProgressMethod.SupplyReceipt || a.ProgressMethod == PcProgressMethod.ServiceDocs) && a.PurchaseOrderLineId.HasValue).Select(a => a.PurchaseOrderLineId.Value).Distinct().ToList();
            var supply = await SupplyAsync(poLineIds, settings, cancellationToken);

            // inspection plan points
            List<int> itpIds = calc.Activities.Where(a => a.ProgressMethod == PcProgressMethod.ItpPoints && a.ItpActivityId.HasValue).Select(a => a.ItpActivityId.Value).ToList();
            var itp = (await _uow.Set<ItpActivity>().AsNoTracking().Where(i => itpIds.Contains(i.Id)).Select(i => new { i.Id, i.Status, i.DoneAt }).ToListAsync(cancellationToken)).ToDictionary(i => i.Id);

            // milestones
            List<int> msOrders = calc.Activities.Where(a => a.ProgressMethod == PcProgressMethod.Milestone && a.WorkOrderId.HasValue).Select(a => a.WorkOrderId.Value).Distinct().ToList();
            var notes = await _uow.Set<ReleaseNote>().AsNoTracking().Where(r => !r.Deleted && r.Status == ReleaseNoteStatus.Released && msOrders.Contains(r.WorkOrderId)).Select(r => new { r.WorkOrderId, r.ReleasedAt }).ToListAsync(cancellationToken);
            var shipments = await _uow.Set<PcShipment>().AsNoTracking().Where(s => !s.Deleted && msOrders.Contains(s.WorkOrderId)).Select(s => new { s.WorkOrderId, s.Status, s.ShippedAt, s.DeliveredAt, s.HandoverAt }).ToListAsync(cancellationToken);

            // issue-driven start: the first confirmed issue to the work order starts its first open construction activity
            List<int> woIds = calc.Activities.Where(a => a.WorkOrderId.HasValue).Select(a => a.WorkOrderId.Value).Distinct().ToList();
            var firstIssue = (await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.Type == InventoryDocumentType.Issue && d.Status == InventoryDocumentStatus.Confirmed && !d.Deleted && d.WorkOrderId != null && woIds.Contains(d.WorkOrderId.Value))
                .GroupBy(d => d.WorkOrderId.Value).Select(g => new { WorkOrderId = g.Key, Date = g.Min(d => d.DocumentDate) }).ToListAsync(cancellationToken)).ToDictionary(x => x.WorkOrderId, x => x.Date.Date);

            decimal engFirst = settings[PcSettingKeys.EngFirstIssue], engSecond = settings[PcSettingKeys.EngSecondIssue], engApproved = settings[PcSettingKeys.EngApproved];
            foreach (PcActivity a in calc.Activities)
            {
                decimal pct = 0m; DateTime? start = null, finish = null;
                switch (a.ProgressMethod)
                {
                    case PcProgressMethod.Manual:
                    case PcProgressMethod.Contractor:
                    case PcProgressMethod.Quantity:
                        var mine = manualRows.Where(m => m.ActivityId == a.Id).ToList();
                        if (mine.Count > 0)
                        {
                            pct = mine[mine.Count - 1].Percent;
                            start = mine[0].DecidedAt?.Date;
                            if (pct >= 100m) finish = mine[mine.Count - 1].DecidedAt?.Date;
                        }
                        break;
                    case PcProgressMethod.EngineeringIssue:
                        if (a.WorkOrderId.HasValue)
                        {
                            var lines = deliverables.Where(d => d.WorkOrderId == a.WorkOrderId.Value && (a.SourceId != 1 || d.ClientSubmittal)).ToList();
                            if (lines.Count > 0)
                            {
                                pct = lines.Average(d => d.IssueState == PcIssueState.Approved ? engApproved : d.IssueState == PcIssueState.SecondIssue ? engSecond : d.IssueState == PcIssueState.FirstIssue ? engFirst : 0m);
                                if (lines.Any(d => d.IssueState != PcIssueState.NotIssued)) start = lines.Where(d => d.LastTransmittalDate.HasValue).Select(d => d.LastTransmittalDate).Min();
                                if (lines.All(d => d.IssueState == PcIssueState.Approved)) finish = lines.Select(d => d.ClientResponseDate ?? d.ApprovedAt?.Date).Max();
                            }
                        }
                        break;
                    case PcProgressMethod.SupplyReceipt:
                    case PcProgressMethod.ServiceDocs:
                        if (a.PurchaseOrderLineId.HasValue && supply.TryGetValue(a.PurchaseOrderLineId.Value, out var s)) { pct = s.Percent; start = s.First; finish = s.Last; }
                        break;
                    case PcProgressMethod.ItpPoints:
                        if (a.ItpActivityId.HasValue && itp.TryGetValue(a.ItpActivityId.Value, out var ia) && (ia.Status == ItpActivityStatus.Done || ia.Status == ItpActivityStatus.Waived || ia.Status == ItpActivityStatus.NotApplicable))
                        { pct = 100m; finish = ia.DoneAt?.Date; }
                        break;
                    case PcProgressMethod.Milestone:
                        DateTime? eventDate = null; bool happened = false;
                        if (a.WorkOrderId.HasValue)
                        {
                            switch (a.MilestoneEvent)
                            {
                                case PcMilestoneEvent.ReleaseNoteReleased:
                                    var rn = notes.Where(n => n.WorkOrderId == a.WorkOrderId.Value).ToList();
                                    happened = rn.Count > 0; eventDate = rn.Select(n => n.ReleasedAt?.Date).Max();
                                    break;
                                case PcMilestoneEvent.ShipmentShipped:
                                    var sh = shipments.Where(x => x.WorkOrderId == a.WorkOrderId.Value && x.Status >= PcShipmentStatus.Shipped).ToList();
                                    happened = sh.Count > 0; eventDate = sh.Select(x => x.ShippedAt).Max();
                                    break;
                                case PcMilestoneEvent.ShipmentDelivered:
                                    var dl = shipments.Where(x => x.WorkOrderId == a.WorkOrderId.Value && x.Status >= PcShipmentStatus.Delivered).ToList();
                                    happened = dl.Count > 0; eventDate = dl.Select(x => x.DeliveredAt).Max();
                                    break;
                                case PcMilestoneEvent.HandoverReceived:
                                    var ho = shipments.Where(x => x.WorkOrderId == a.WorkOrderId.Value && x.Status == PcShipmentStatus.HandoverReceived).ToList();
                                    happened = ho.Count > 0; eventDate = ho.Select(x => x.HandoverAt).Max();
                                    break;
                            }
                        }
                        if (happened) { pct = 100m; finish = eventDate ?? asOf.Date; start = finish; }
                        break;
                }
                calc.Percent[a.Id] = Math.Min(100m, Math.Max(0m, pct));
                calc.StartDate[a.Id] = start; calc.FinishDate[a.Id] = finish;
            }

            // issue-driven actual start
            foreach (int wo in woIds)
            {
                if (!firstIssue.TryGetValue(wo, out DateTime issueDate) || issueDate > asOf.Date) continue;
                PcActivity target = calc.Activities.Where(a => a.WorkOrderId == wo && a.SourceType == PcSourceType.Process && a.Phase == PcPhase.Construction && calc.Percent[a.Id] < 100m).OrderBy(a => a.Sort).FirstOrDefault();
                if (target != null && !calc.StartDate[target.Id].HasValue && !target.ActualStart.HasValue) calc.StartDate[target.Id] = issueDate;
            }

            // hierarchical weights
            List<PcWbsNode> nodes = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == contractId && !n.Deleted).ToListAsync(cancellationToken);
            calc.Weight = EffectiveWeights(nodes, calc.Activities);
            return calc;
        }

        /// <summary>Weight of every activity in the whole project (the fractions add up to one); siblings are normalised, branches without activities are ignored.</summary>
        internal static Dictionary<int, decimal> EffectiveWeights(List<PcWbsNode> nodes, List<PcActivity> acts)
        {
            var result = new Dictionary<int, decimal>();
            if (acts.Count == 0) return result;
            var nodeIds = new HashSet<int>(nodes.Select(n => n.Id));
            var childNodes = nodes.Where(n => n.ParentId.HasValue && nodeIds.Contains(n.ParentId.Value)).GroupBy(n => n.ParentId.Value).ToDictionary(g => g.Key, g => g.ToList());
            var childActs = acts.Where(a => a.WbsNodeId.HasValue && nodeIds.Contains(a.WbsNodeId.Value)).GroupBy(a => a.WbsNodeId.Value).ToDictionary(g => g.Key, g => g.ToList());
            var hasActs = new Dictionary<int, bool>();
            bool Has(PcWbsNode n)
            {
                if (hasActs.TryGetValue(n.Id, out bool v)) return v;
                bool r = childActs.ContainsKey(n.Id) || (childNodes.TryGetValue(n.Id, out List<PcWbsNode> cn) && cn.Any(Has));
                hasActs[n.Id] = r;
                return r;
            }
            void Distribute(List<PcWbsNode> ns, List<PcActivity> aa, decimal fraction)
            {
                ns = ns.Where(Has).ToList();
                int count = ns.Count + aa.Count;
                if (count == 0) return;
                decimal total = ns.Sum(n => n.Weight) + aa.Sum(a => a.Weight);
                foreach (PcWbsNode n in ns)
                {
                    decimal f = fraction * (total > 0 ? n.Weight / total : 1m / count);
                    Distribute(childNodes.TryGetValue(n.Id, out List<PcWbsNode> cn) ? cn : new List<PcWbsNode>(), childActs.TryGetValue(n.Id, out List<PcActivity> ca) ? ca : new List<PcActivity>(), f);
                }
                foreach (PcActivity a in aa) result[a.Id] = fraction * (total > 0 ? a.Weight / total : 1m / count);
            }
            List<PcWbsNode> roots = nodes.Where(n => !n.ParentId.HasValue || !nodeIds.Contains(n.ParentId.Value)).ToList();
            List<PcActivity> orphans = acts.Where(a => !a.WbsNodeId.HasValue || !nodeIds.Contains(a.WbsNodeId.Value)).ToList();
            Distribute(roots, orphans, 1m);
            return result;
        }

        public async Task<decimal> PercentAsync(int contractId, int? workOrderId, CancellationToken cancellationToken)
        {
            Calc c = await ComputeAsync(contractId, false, DateTime.Today, cancellationToken);
            IEnumerable<PcActivity> list = c.Activities;
            if (workOrderId.HasValue) list = list.Where(a => a.WorkOrderId == workOrderId.Value);
            List<PcActivity> acts = list.ToList();
            if (acts.Count == 0) return 0m;
            decimal total = acts.Sum(a => c.Weight.TryGetValue(a.Id, out decimal w) ? w : 0m);
            if (total <= 0m) return PcText.Round2(acts.Average(a => c.Percent[a.Id]));
            return PcText.Round2(acts.Sum(a => (c.Weight.TryGetValue(a.Id, out decimal w) ? w : 0m) * c.Percent[a.Id]) / total);
        }

        public async Task<List<PcActivity>> ApplyAsync(int contractId, CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            Calc c = await ComputeAsync(contractId, true, today, cancellationToken);
            foreach (PcActivity a in c.Activities)
            {
                decimal pct = c.Percent[a.Id];
                a.PercentCache = pct;
                if (a.Status == PcActivityStatus.OnHold && pct < 100m) continue;
                DateTime? s = c.StartDate[a.Id], f = c.FinishDate[a.Id];
                if (pct > 0m && !a.ActualStart.HasValue) a.ActualStart = s ?? today;
                else if (!a.ActualStart.HasValue && s.HasValue && a.Status == PcActivityStatus.NotStarted) { a.ActualStart = s; a.Status = PcActivityStatus.InProgress; }
                if (pct >= 100m)
                {
                    a.Status = PcActivityStatus.Done;
                    if (!a.ActualFinish.HasValue) a.ActualFinish = f ?? today;
                    if (!a.ActualStart.HasValue) a.ActualStart = a.ActualFinish;
                }
                else
                {
                    if (a.Status == PcActivityStatus.Done) { a.Status = PcActivityStatus.InProgress; a.ActualFinish = null; }
                    if (a.ActualStart.HasValue && a.Status == PcActivityStatus.NotStarted) a.Status = PcActivityStatus.InProgress;
                }
            }
            return c.Activities;
        }

        public async Task<ResponseDto<PcProgressDto>> GetProgressAsync(int contractId, CancellationToken cancellationToken)
        {
            ContractEntity contract = await _uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId && !c.Deleted, cancellationToken);
            if (contract == null) return PcText.NotFound<PcProgressDto>(PcText.ContractNotFound);
            DateTime asOf = DateTime.Today;
            Calc c = await ComputeAsync(contractId, false, asOf, cancellationToken);
            PcPlanSet plan = await LoadPlanAsync(contractId, c.Activities, cancellationToken);
            PcWorkCalendar cal = plan.Calendar;
            var dto = new PcProgressDto { ContractId = contractId, AsOf = asOf, Activities = c.Activities.Count, DoneActivities = c.Activities.Count(a => a.Status == PcActivityStatus.Done), LateActivities = c.Activities.Count(a => a.IsLate) };
            if (c.Activities.Count == 0) return ResponseDtoHelper.Ok(dto);
            decimal PlannedOf(PcActivity a) => plan.Items.TryGetValue(a.Id, out PcPlanItem it) ? plan.Fraction(it, asOf) * 100m : 0m;
            decimal Weighted(IEnumerable<PcActivity> list, Func<PcActivity, decimal> value)
            {
                List<PcActivity> l = list.ToList();
                decimal total = l.Sum(a => c.Weight.TryGetValue(a.Id, out decimal w) ? w : 0m);
                if (l.Count == 0) return 0m;
                if (total <= 0m) return PcText.Round2(l.Average(value));
                return PcText.Round2(l.Sum(a => (c.Weight.TryGetValue(a.Id, out decimal w) ? w : 0m) * value(a)) / total);
            }
            dto.PhysicalPercent = Weighted(c.Activities, a => c.Percent[a.Id]);
            dto.PlannedPercent = Weighted(c.Activities, PlannedOf);
            foreach (var g in c.Activities.GroupBy(a => a.Phase).OrderBy(g => g.Key))
                dto.ByPhase.Add(new PcPhaseProgressDto { Phase = g.Key, Activities = g.Count(), Percent = Weighted(g, a => c.Percent[a.Id]), PlannedPercent = Weighted(g, PlannedOf) });
            Dictionary<int, string> codes = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.ContractId == contractId).ToDictionaryAsync(w => w.Id, w => w.Code, cancellationToken);
            foreach (var g in c.Activities.GroupBy(a => a.WorkOrderId).OrderBy(g => g.Key))
                dto.ByWorkOrder.Add(new PcWorkOrderProgressDto { WorkOrderId = g.Key, WorkOrderCode = g.Key.HasValue && codes.TryGetValue(g.Key.Value, out string code) ? code : null, Activities = g.Count(), Percent = Weighted(g, a => c.Percent[a.Id]) });
            return ResponseDtoHelper.Ok(dto);
        }

        // ============================================================================ manual progress

        public async Task<ResponseDto<PagedList<PcManualProgressDto>>> GetManualAsync(PcManualQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcManualQuery();
            IQueryable<PcManualProgress> q = _uow.Set<PcManualProgress>().AsNoTracking();
            if (query.ContractId.HasValue) q = q.Where(m => m.Activity.ContractId == query.ContractId.Value);
            if (query.ActivityId.HasValue) q = q.Where(m => m.ActivityId == query.ActivityId.Value);
            if (query.State.HasValue) q = q.Where(m => m.State == query.State.Value);
            PagedList<PcManualProgress> page = await PagedList<PcManualProgress>.CreateAsync(q.Include(m => m.Activity).OrderByDescending(m => m.Id), query.PageNumber, query.PageSize, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, page.Select(m => (int?)m.ProposedByUserId).Concat(page.Select(m => m.DecidedByUserId)), cancellationToken);
            List<PcManualProgressDto> dtos = page.Select(m => ToDto(m, names)).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcManualProgressDto>)new PcPage<PcManualProgressDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        private static PcManualProgressDto ToDto(PcManualProgress m, Dictionary<int, string> names) => new PcManualProgressDto
        {
            Id = m.Id, ActivityId = m.ActivityId, ActivityCode = m.Activity?.Code, ActivityTitle = m.Activity?.Title, Percent = m.Percent, Comment = m.Comment, ProposedByUserId = m.ProposedByUserId,
            ProposedByName = names.Name(m.ProposedByUserId), ProposedAt = m.ProposedAt, State = m.State, DecidedByName = names.Name(m.DecidedByUserId), DecidedAt = m.DecidedAt, DecisionNote = m.DecisionNote,
        };

        public async Task<ResponseDto<PcManualProgressDto>> ProposeManualAsync(PcManualProgressSaveDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcManualProgressDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.UnitManagers + ",PMO")) return Bad<PcManualProgressDto>(PcText.NotAllowed);
            if (dto.Percent < 0m || dto.Percent > 100m) return Bad<PcManualProgressDto>("درصد پیشرفت باید بین ۰ و ۱۰۰ باشد.");
            PcActivity a = await _uow.Set<PcActivity>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.ActivityId && !x.Deleted, cancellationToken);
            if (a == null) return PcText.NotFound<PcManualProgressDto>("فعالیت پیدا نشد.");
            if (a.ProgressMethod != PcProgressMethod.Manual && a.ProgressMethod != PcProgressMethod.Contractor && a.ProgressMethod != PcProgressMethod.Quantity) return Bad<PcManualProgressDto>("پیشرفت این فعالیت از رویدادهای سیستم محاسبه می‌شود و دستی ثبت نمی‌شود.");
            if (await _uow.Set<PcManualProgress>().AnyAsync(m => m.ActivityId == a.Id && m.State == 0, cancellationToken)) return Bad<PcManualProgressDto>("یک پیشنهاد در انتظار تایید برنامه‌ریزی وجود دارد.");
            var row = new PcManualProgress { ActivityId = a.Id, Percent = PcText.Round2(dto.Percent), Comment = Clean(dto.Comment), ProposedByUserId = Me, ProposedAt = DateTimeOffset.Now, State = 0 };
            _uow.Set<PcManualProgress>().Add(row);
            await _uow.SaveChangesAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { Me }, cancellationToken);
            row.Activity = a;
            return ResponseDtoHelper.Ok(ToDto(row, names));
        }

        public async Task<ResponseDto<PcManualProgressDto>> DecideManualAsync(PcManualDecisionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcManualProgressDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcManualProgressDto>(PcText.NotAllowed + " (تایید پیشرفت با مدیر برنامه‌ریزی است)");
            PcManualProgress m = await _uow.Set<PcManualProgress>().Include(x => x.Activity).FirstOrDefaultAsync(x => x.Id == dto.Id, cancellationToken);
            if (m == null) return PcText.NotFound<PcManualProgressDto>("پیشنهاد پیشرفت پیدا نشد.");
            if (m.State != 0) return Bad<PcManualProgressDto>("درباره این پیشنهاد تصمیم گرفته شده است.");
            if (m.ProposedByUserId == Me) return Bad<PcManualProgressDto>(PcText.SameUser);
            if (!dto.Approve && Clean(dto.Note) == null) return Bad<PcManualProgressDto>("دلیل رد را بنویسید.");
            m.State = dto.Approve ? 1 : 2; m.DecidedByUserId = Me; m.DecidedAt = DateTimeOffset.Now; m.DecisionNote = Clean(dto.Note);
            await _uow.SaveChangesAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { m.ProposedByUserId, m.DecidedByUserId }, cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(m, names));
        }

        // ============================================================================ plan, earned value

        private sealed class PcPlanItem
        {
            public int ActivityId;
            public DateTime Start;
            public DateTime Finish;
            public decimal Budget;
            public int Duration;
            public int StartOffset;
            public int FinishOffset;
        }

        private sealed class PcPlanSet
        {
            public bool FromBaseline;
            public int? BaselineId;
            public decimal Bac;
            public decimal ManagementReserve;
            public DateTime? Start;
            public DateTime? Finish;
            public PcWorkCalendar Calendar;
            public PcAccrualMode Mode;
            public Dictionary<int, PcPlanItem> Items = new Dictionary<int, PcPlanItem>();

            public int OffsetAt(DateTime asOf)
            {
                if (!Start.HasValue || asOf.Date < Calendar.Origin) return -1;
                return Calendar.OffsetOf(Calendar.PreviousWorkingOnOrBefore(asOf));
            }

            public decimal FractionAt(PcPlanItem it, int t)
            {
                if (t < it.StartOffset) return 0m;
                if (t >= it.FinishOffset) return 1m;
                if (Mode == PcAccrualMode.AtEnd || it.Duration == 0) return 0m;
                int d = it.FinishOffset - it.StartOffset + 1;
                return Math.Min(1m, Math.Max(0m, (decimal)(t - it.StartOffset + 1) / d));
            }

            public decimal Fraction(PcPlanItem it, DateTime asOf) => FractionAt(it, OffsetAt(asOf));

            public decimal PvAt(int t) => Items.Values.Sum(i => i.Budget * FractionAt(i, t));
        }

        private async Task<PcPlanSet> LoadPlanAsync(int contractId, List<PcActivity> acts, CancellationToken cancellationToken)
        {
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            var plan = new PcPlanSet { Mode = terms?.AccrualMode ?? PcAccrualMode.Linear, Calendar = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken) };
            if (terms?.CurrentBaselineId != null)
            {
                PcBaseline b = await _uow.Set<PcBaseline>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == terms.CurrentBaselineId.Value && !x.Deleted, cancellationToken);
                if (b != null)
                {
                    plan.FromBaseline = true; plan.BaselineId = b.Id; plan.Bac = b.Bac; plan.ManagementReserve = b.ManagementReserve;
                    List<PcBaselineActivity> rows = await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(x => x.BaselineId == b.Id).ToListAsync(cancellationToken);
                    foreach (PcBaselineActivity r in rows) plan.Items[r.ActivityId] = new PcPlanItem { ActivityId = r.ActivityId, Start = r.Start.Date, Finish = r.Finish.Date, Budget = r.Budget, Duration = r.DurationDays };
                }
            }
            if (!plan.FromBaseline)
            {
                plan.ManagementReserve = terms?.ManagementReserve ?? 0m;
                foreach (PcActivity a in acts.Where(a => a.EarlyStart.HasValue && a.EarlyFinish.HasValue))
                    plan.Items[a.Id] = new PcPlanItem { ActivityId = a.Id, Start = a.EarlyStart.Value.Date, Finish = a.EarlyFinish.Value.Date, Budget = a.BudgetAtCompletion, Duration = a.DurationDays };
                plan.Bac = plan.Items.Values.Sum(i => i.Budget);
            }
            if (plan.Items.Count > 0)
            {
                plan.Start = plan.Items.Values.Min(i => i.Start); plan.Finish = plan.Items.Values.Max(i => i.Finish);
                plan.Calendar.SetOrigin(plan.Start.Value);
                foreach (PcPlanItem i in plan.Items.Values) { i.StartOffset = plan.Calendar.OffsetOf(i.Start); i.FinishOffset = plan.Calendar.OffsetOf(i.Finish); }
            }
            return plan;
        }

        private async Task<decimal> ActualCostAsync(int contractId, DateTime asOf, CancellationToken cancellationToken)
        {
            DateTime end = asOf.Date.AddDays(1);
            return await _uow.Set<CostEntry>().AsNoTracking().Where(e => e.EntryDate < end && (e.ContractId == contractId || (e.ContractId == null && e.WorkOrder != null && e.WorkOrder.ContractId == contractId))).SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
        }

        public async Task<PcEvmDto> ComputeEvmAsync(int contractId, DateTime asOf, CancellationToken cancellationToken)
        {
            asOf = asOf.Date;
            Calc c = await ComputeAsync(contractId, false, asOf, cancellationToken);
            return await EvmFromAsync(contractId, asOf, c, cancellationToken);
        }

        private async Task<PcEvmDto> EvmFromAsync(int contractId, DateTime asOf, Calc c, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            PcPlanSet plan = await LoadPlanAsync(contractId, c.Activities, cancellationToken);
            var dto = new PcEvmDto { ContractId = contractId, AsOf = asOf, BaselineId = plan.BaselineId, FromBaseline = plan.FromBaseline, Bac = plan.Bac, ManagementReserve = plan.ManagementReserve, SpiStatus = "none", CpiStatus = "none" };
            if (plan.Items.Count == 0) { dto.Note = "مبنای زمان‌بندی یا برنامه محاسبه‌شده‌ای وجود ندارد."; dto.Ac = await ActualCostAsync(contractId, asOf, cancellationToken); return dto; }
            int t = plan.OffsetAt(asOf);
            decimal pv = 0m, ev = 0m;
            foreach (PcPlanItem it in plan.Items.Values)
            {
                pv += it.Budget * plan.FractionAt(it, t);
                ev += c.Percent.TryGetValue(it.ActivityId, out decimal p) ? it.Budget * p / 100m : 0m;
            }
            dto.Pv = PcText.Round2(pv); dto.Ev = PcText.Round2(ev); dto.Ac = PcText.Round2(await ActualCostAsync(contractId, asOf, cancellationToken));
            dto.Cv = dto.Ev - dto.Ac; dto.Sv = dto.Ev - dto.Pv;
            if (dto.Ac > 0m) dto.Cpi = PcText.Round4(dto.Ev / dto.Ac);
            if (dto.Pv > 0m) dto.Spi = PcText.Round4(dto.Ev / dto.Pv);

            // earned schedule (working days)
            int last = plan.Items.Values.Max(i => i.FinishOffset);
            int at = t < 0 ? 0 : t + 1;
            decimal es = 0m;
            if (dto.Ev > 0m)
            {
                decimal prev = 0m;
                es = last + 1;
                for (int k = 0; k <= last; k++)
                {
                    decimal cur = plan.PvAt(k);
                    if (cur >= dto.Ev) { es = k + (cur > prev ? (dto.Ev - prev) / (cur - prev) : 0m); break; }
                    prev = cur;
                }
            }
            dto.ActualTimeDays = at; dto.EarnedScheduleDays = PcText.Round2(es); dto.ScheduleVarianceDays = PcText.Round2(es - at);
            if (at > 0) dto.SpiTime = PcText.Round4(es / at);

            decimal bac = dto.Bac;
            if (dto.Cpi.HasValue && dto.Cpi.Value > 0m) { dto.EacTypical = PcText.Round2(bac / dto.Cpi.Value); dto.EtcTypical = PcText.Round2(dto.EacTypical.Value - dto.Ac); dto.VacTypical = PcText.Round2(bac - dto.EacTypical.Value); }
            dto.EacAtypical = PcText.Round2(dto.Ac + (bac - dto.Ev));
            if (dto.Cpi.HasValue && dto.Spi.HasValue && dto.Cpi.Value * dto.Spi.Value > 0m) dto.EacCpiSpi = PcText.Round2(dto.Ac + (bac - dto.Ev) / (dto.Cpi.Value * dto.Spi.Value));
            if (bac - dto.Ac != 0m) dto.Tcpi = PcText.Round4((bac - dto.Ev) / (bac - dto.Ac));
            if (dto.EacTypical.HasValue && dto.EacTypical.Value - dto.Ac != 0m) dto.TcpiEac = PcText.Round4((bac - dto.Ev) / (dto.EacTypical.Value - dto.Ac));

            decimal amber = settings[PcSettingKeys.AmberThreshold], red = settings[PcSettingKeys.RedThreshold], action = settings[PcSettingKeys.ActionThreshold];
            string Status(decimal? v) => !v.HasValue ? "none" : v.Value < red ? "red" : v.Value < amber ? "amber" : "green";
            dto.SpiStatus = Status(dto.Spi); dto.CpiStatus = Status(dto.Cpi);
            dto.CorrectiveActionRequired = (dto.Spi.HasValue && dto.Spi.Value < action) || (dto.Cpi.HasValue && dto.Cpi.Value < action);

            // physical and planned percent (weighted)
            decimal totalW = c.Weight.Values.Sum();
            decimal Weighted(Func<PcActivity, decimal> value) => totalW <= 0m ? (c.Activities.Count == 0 ? 0m : c.Activities.Average(value)) : c.Activities.Sum(a => (c.Weight.TryGetValue(a.Id, out decimal w) ? w : 0m) * value(a)) / totalW;
            dto.PhysicalPercent = PcText.Round2(Weighted(a => c.Percent[a.Id]));
            dto.PlannedPercent = PcText.Round2(Weighted(a => plan.Items.TryGetValue(a.Id, out PcPlanItem it) ? plan.FractionAt(it, t) * 100m : 0m));
            decimal delay = dto.PlannedPercent - dto.PhysicalPercent;
            dto.RescheduleRequired = delay > settings[PcSettingKeys.RescheduleDelayPercent];
            dto.BaselineFinish = plan.Finish;
            dto.ForecastFinish = c.Activities.Where(a => a.ForecastFinish.HasValue).Select(a => a.ForecastFinish).DefaultIfEmpty().Max();
            return dto;
        }

        public async Task<ResponseDto<PcEvmDto>> GetEvmAsync(int contractId, DateTime? asOf, CancellationToken cancellationToken)
        {
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == contractId && !x.Deleted, cancellationToken)) return PcText.NotFound<PcEvmDto>(PcText.ContractNotFound);
            return ResponseDtoHelper.Ok(await ComputeEvmAsync(contractId, asOf ?? DateTime.Today, cancellationToken));
        }

        public async Task<ResponseDto<List<PcCashFlowPointDto>>> GetCashFlowAsync(int contractId, CancellationToken cancellationToken)
        {
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == contractId && !x.Deleted, cancellationToken)) return PcText.NotFound<List<PcCashFlowPointDto>>(PcText.ContractNotFound);
            List<PcActivity> acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted).ToListAsync(cancellationToken);
            PcPlanSet plan = await LoadPlanAsync(contractId, acts, cancellationToken);
            var points = new List<PcCashFlowPointDto>();
            if (plan.Items.Count == 0) return ResponseDtoHelper.Ok(points);
            List<PcProgressSnapshot> snaps = await _uow.Set<PcProgressSnapshot>().AsNoTracking().Where(s => s.ContractId == contractId).OrderBy(s => s.DataDate).ToListAsync(cancellationToken);
            DateTime cursor = new DateTime(plan.Start.Value.Year, plan.Start.Value.Month, 1).AddMonths(1).AddDays(-1);
            DateTime lastDate = plan.Finish.Value.AddDays(1);
            for (int i = 0; i < 240; i++)
            {
                DateTime point = cursor > plan.Finish.Value ? plan.Finish.Value : cursor;
                int t = plan.OffsetAt(point);
                PcProgressSnapshot snap = snaps.LastOrDefault(s => s.DataDate <= point);
                points.Add(new PcCashFlowPointDto { Date = point, PlannedCumulative = PcText.Round2(plan.PvAt(t)), ActualCostCumulative = PcText.Round2(await ActualCostAsync(contractId, point, cancellationToken)), EarnedValue = snap?.Ev });
                if (cursor >= lastDate || point == plan.Finish.Value) break;
                cursor = new DateTime(cursor.Year, cursor.Month, 1).AddMonths(2).AddDays(-1);
            }
            return ResponseDtoHelper.Ok(points);
        }

        // ============================================================================ cost entries (project control)

        public async Task<ResponseDto<PcCostEntryDto>> AddCostAsync(PcCostEntrySaveDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcCostEntryDto>(PcText.InvalidData);
            if (!_user.IsInRole("FINANCIAL,PMO")) return Bad<PcCostEntryDto>(PcText.NotAllowed);
            if (!Enum.IsDefined(typeof(CostKind), dto.Kind)) return Bad<PcCostEntryDto>("نوع هزینه را مشخص کنید.");
            if (dto.Amount == 0m) return Bad<PcCostEntryDto>("مبلغ را وارد کنید.");
            if (Clean(dto.Description) == null) return Bad<PcCostEntryDto>("شرح هزینه را بنویسید.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken)) return PcText.NotFound<PcCostEntryDto>(PcText.ContractNotFound);
            if (dto.WorkOrderId.HasValue && !await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId.Value && w.ContractId == dto.ContractId && !w.Deleted, cancellationToken)) return Bad<PcCostEntryDto>("دستور کار متعلق به این قرارداد نیست.");
            if (dto.WbsNodeId.HasValue && !await _uow.Set<PcWbsNode>().AnyAsync(n => n.Id == dto.WbsNodeId.Value && n.ContractId == dto.ContractId && !n.Deleted, cancellationToken)) return Bad<PcCostEntryDto>("گره WBS متعلق به این قرارداد نیست.");
            var e = new CostEntry
            {
                EntryDate = dto.EntryDate == default ? DateTime.Today : dto.EntryDate.Date, Kind = dto.Kind, ContractId = dto.ContractId, WorkOrderId = dto.WorkOrderId, WbsNodeId = dto.WbsNodeId, Amount = PcText.Round2(dto.Amount),
                Quantity = 0m, SourceType = "ProjectControl", SourceId = 0, SourceNumber = "PC", Description = Clean(dto.Description), CreatedAt = DateTimeOffset.Now, CreatedByUserId = Me,
            };
            _uow.Set<CostEntry>().Add(e);
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(new PcCostEntryDto { Id = e.Id, ContractId = e.ContractId, WorkOrderId = e.WorkOrderId, WbsNodeId = e.WbsNodeId, Kind = e.Kind, EntryDate = e.EntryDate, Amount = e.Amount, SourceType = e.SourceType, SourceNumber = e.SourceNumber, Description = e.Description });
        }

        public async Task<ResponseDto<PagedList<PcCostEntryDto>>> GetCostsAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            if (!query.ContractId.HasValue) return Bad<PagedList<PcCostEntryDto>>("قرارداد را مشخص کنید.");
            int id = query.ContractId.Value;
            IQueryable<PcCostEntryDto> projection = _uow.Set<CostEntry>().AsNoTracking().Where(e => e.ContractId == id || (e.ContractId == null && e.WorkOrder != null && e.WorkOrder.ContractId == id))
                .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id).Select(e => new PcCostEntryDto
                {
                    Id = e.Id, ContractId = e.ContractId, WorkOrderId = e.WorkOrderId, WbsNodeId = e.WbsNodeId, Kind = e.Kind, EntryDate = e.EntryDate, Amount = e.Amount, SourceType = e.SourceType, SourceNumber = e.SourceNumber, Description = e.Description,
                });
            return ResponseDtoHelper.Ok(await PagedList<PcCostEntryDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        // ============================================================================ weekly snapshots

        private static string HashOf(PcProgressSnapshot s)
        {
            var sb = new StringBuilder();
            sb.Append(s.ContractId).Append('|').Append(s.DataDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|').Append(s.BaselineId).Append('|')
              .Append(s.PhysicalPercent.ToString(CultureInfo.InvariantCulture)).Append('|').Append(s.PlannedPercent.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(s.Bac.ToString(CultureInfo.InvariantCulture)).Append('|').Append(s.Pv.ToString(CultureInfo.InvariantCulture)).Append('|').Append(s.Ev.ToString(CultureInfo.InvariantCulture)).Append('|').Append(s.Ac.ToString(CultureInfo.InvariantCulture));
            foreach (PcProgressSnapshotLine l in s.Lines.OrderBy(x => x.ActivityId))
                sb.Append(';').Append(l.ActivityId).Append(',').Append(l.Percent.ToString(CultureInfo.InvariantCulture)).Append(',').Append(l.Budget.ToString(CultureInfo.InvariantCulture)).Append(',').Append(l.Ev.ToString(CultureInfo.InvariantCulture)).Append(',').Append(l.Pv.ToString(CultureInfo.InvariantCulture));
            return PcText.Sha256(sb.ToString());
        }

        private async Task<(PcProgressSnapshot Snapshot, string Error)> BuildSnapshotAsync(int contractId, DateTime dataDate, int userId, CancellationToken cancellationToken)
        {
            if (await _uow.Set<PcProgressSnapshot>().AnyAsync(s => s.ContractId == contractId && s.DataDate == dataDate, cancellationToken)) return (null, "برای این تاریخ داده، عکس هفتگی قبلاً گرفته شده و تغییر نمی‌کند.");
            List<PcActivity> acts = await ApplyAsync(contractId, cancellationToken);
            if (acts.Count == 0) return (null, "فعالیتی برای عکس هفتگی وجود ندارد.");
            Calc c = await ComputeAsync(contractId, false, dataDate, cancellationToken);
            PcEvmDto evm = await EvmFromAsync(contractId, dataDate, c, cancellationToken);
            PcPlanSet plan = await LoadPlanAsync(contractId, c.Activities, cancellationToken);
            int t = plan.OffsetAt(dataDate);
            var snap = new PcProgressSnapshot
            {
                ContractId = contractId, DataDate = dataDate, TakenAt = DateTimeOffset.Now, TakenByUserId = userId, BaselineId = evm.BaselineId, PhysicalPercent = evm.PhysicalPercent, PlannedPercent = evm.PlannedPercent,
                Bac = evm.Bac, Pv = evm.Pv, Ev = evm.Ev, Ac = evm.Ac, Cpi = evm.Cpi, Spi = evm.Spi, ForecastFinish = evm.ForecastFinish, Lines = new List<PcProgressSnapshotLine>(),
            };
            foreach (PcActivity a in c.Activities)
            {
                decimal pct = c.Percent[a.Id];
                decimal budget = 0m, pv = 0m, planned = 0m;
                if (plan.Items.TryGetValue(a.Id, out PcPlanItem it)) { budget = it.Budget; planned = plan.FractionAt(it, t) * 100m; pv = it.Budget * plan.FractionAt(it, t); }
                snap.Lines.Add(new PcProgressSnapshotLine { ActivityId = a.Id, Percent = pct, PlannedPercent = PcText.Round2(planned), Budget = budget, Ev = PcText.Round2(budget * pct / 100m), Pv = PcText.Round2(pv), ForecastFinish = a.ForecastFinish });
            }
            snap.Hash = HashOf(snap);
            _uow.Set<PcProgressSnapshot>().Add(snap);
            return (snap, null);
        }

        private static PcSnapshotDto ToDto(PcProgressSnapshot s, Dictionary<int, string> codes) => new PcSnapshotDto
        {
            Id = s.Id, ContractId = s.ContractId, DataDate = s.DataDate, TakenAt = s.TakenAt, TakenByUserId = s.TakenByUserId, BaselineId = s.BaselineId, PhysicalPercent = s.PhysicalPercent,
            PlannedPercent = s.PlannedPercent, Bac = s.Bac, Pv = s.Pv, Ev = s.Ev, Ac = s.Ac, Cpi = s.Cpi, Spi = s.Spi, ForecastFinish = s.ForecastFinish, Hash = s.Hash,
            Lines = (s.Lines ?? new List<PcProgressSnapshotLine>()).OrderBy(l => l.ActivityId).Select(l => new PcSnapshotLineDto
            {
                ActivityId = l.ActivityId, ActivityCode = codes != null && codes.TryGetValue(l.ActivityId, out string code) ? code : null, Percent = l.Percent, PlannedPercent = l.PlannedPercent, Budget = l.Budget, Ev = l.Ev, Pv = l.Pv, ForecastFinish = l.ForecastFinish,
            }).ToList(),
        };

        public async Task<ResponseDto<PcSnapshotDto>> TakeSnapshotAsync(PcSnapshotRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcSnapshotDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcSnapshotDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken)) return PcText.NotFound<PcSnapshotDto>(PcText.ContractNotFound);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, dto.ContractId, false, cancellationToken);
            DateTime dataDate = (dto.DataDate ?? PcText.LastWeekday(DateTime.Today, terms?.DataDateWeekday ?? 3)).Date;
            if (dataDate > DateTime.Today) return Bad<PcSnapshotDto>("تاریخ داده در آینده است.");
            var built = await BuildSnapshotAsync(dto.ContractId, dataDate, Me, cancellationToken);
            if (built.Error != null) return Bad<PcSnapshotDto>(built.Error);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcSnapshotDto>("برای این تاریخ داده، عکس هفتگی قبلاً گرفته شده است."); }
            Dictionary<int, string> codes = built.Snapshot.Lines.Count > 0 ? await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == dto.ContractId).ToDictionaryAsync(a => a.Id, a => a.Code, cancellationToken) : null;
            return ResponseDtoHelper.Ok(ToDto(built.Snapshot, codes));
        }

        public async Task<ResponseDto<PcSnapshotRunResultDto>> RunDueSnapshotsAsync(PcSnapshotRunDto dto, CancellationToken cancellationToken)
        {
            // the server job calls this without a user; a person needs the PMO / planning role
            if (Me != 0 && !_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcSnapshotRunResultDto>(PcText.NotAllowed);
            var result = new PcSnapshotRunResultDto();
            DateTime now = dto?.AsOf ?? DateTime.Now;
            bool simulated = dto?.AsOf != null;
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            List<PcContractTerms> list = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.AutoSnapshot && t.CurrentBaselineId != null).OrderBy(t => t.ContractId).ToListAsync(cancellationToken);
            foreach (PcContractTerms t in list)
            {
                result.ContractsChecked++;
                DateTime dataDate = PcText.LastWeekday(now, t.DataDateWeekday);
                if (!simulated && dataDate == now.Date && now.Hour < (int)settings[PcSettingKeys.SnapshotHour]) continue;
                if (await _uow.Set<PcProgressSnapshot>().AnyAsync(s => s.ContractId == t.ContractId && s.DataDate == dataDate, cancellationToken)) continue;
                try
                {
                    var built = await BuildSnapshotAsync(t.ContractId, dataDate, 0, cancellationToken);
                    if (built.Error != null) { result.Notes.Add(t.ContractId + ": " + built.Error); continue; }
                    await _uow.SaveChangesAsync(cancellationToken);
                    result.SnapshotsTaken++;
                }
                catch (DbUpdateException) { result.Notes.Add(t.ContractId + ": عکس هفتگی در همین لحظه توسط فرآیند دیگری گرفته شد."); }
            }
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<PagedList<PcSnapshotDto>>> GetSnapshotsAsync(PcSnapshotQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcSnapshotQuery();
            IQueryable<PcProgressSnapshot> q = _uow.Set<PcProgressSnapshot>().AsNoTracking();
            if (query.ContractId.HasValue) q = q.Where(s => s.ContractId == query.ContractId.Value);
            if (query.From.HasValue) q = q.Where(s => s.DataDate >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(s => s.DataDate <= query.To.Value.Date);
            IQueryable<PcSnapshotDto> projection = q.OrderByDescending(s => s.DataDate).ThenByDescending(s => s.Id).Select(s => new PcSnapshotDto
            {
                Id = s.Id, ContractId = s.ContractId, DataDate = s.DataDate, TakenAt = s.TakenAt, TakenByUserId = s.TakenByUserId, BaselineId = s.BaselineId, PhysicalPercent = s.PhysicalPercent,
                PlannedPercent = s.PlannedPercent, Bac = s.Bac, Pv = s.Pv, Ev = s.Ev, Ac = s.Ac, Cpi = s.Cpi, Spi = s.Spi, ForecastFinish = s.ForecastFinish, Hash = s.Hash,
            });
            return ResponseDtoHelper.Ok(await PagedList<PcSnapshotDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken));
        }

        public async Task<ResponseDto<PcSnapshotDto>> GetSnapshotAsync(int id, CancellationToken cancellationToken)
        {
            PcProgressSnapshot s = await _uow.Set<PcProgressSnapshot>().AsNoTracking().Include(x => x.Lines).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (s == null) return PcText.NotFound<PcSnapshotDto>("عکس هفتگی پیدا نشد.");
            Dictionary<int, string> codes = await _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == s.ContractId).ToDictionaryAsync(a => a.Id, a => a.Code, cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(s, codes));
        }

        // ============================================================================ daily reports (R-PR-4)

        private async Task<PcDailyReportDto> DailyDtoAsync(PcDailyReport r, CancellationToken cancellationToken)
        {
            List<int> ids = r.Lines.Select(l => l.ActivityId).ToList();
            var acts = await _uow.Set<PcActivity>().AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.Code, a.Title }).ToListAsync(cancellationToken);
            return new PcDailyReportDto
            {
                Id = r.Id, ContractId = r.ContractId, Unit = r.Unit, ReportDate = r.ReportDate, State = r.State, ConfirmedAt = r.ConfirmedAt,
                Lines = r.Lines.OrderBy(l => l.Id).Select(l => new PcDailyLineDto
                {
                    ActivityId = l.ActivityId, ActivityCode = acts.FirstOrDefault(a => a.Id == l.ActivityId)?.Code, ActivityTitle = acts.FirstOrDefault(a => a.Id == l.ActivityId)?.Title, EventText = l.EventText,
                    Percent = l.Percent, IsLate = l.IsLate, DelayReasonCode = l.DelayReasonCode, DelayReason = l.DelayReason, PreventiveAction = l.PreventiveAction, CorrectiveAction = l.CorrectiveAction,
                }).ToList(),
            };
        }

        public async Task<ResponseDto<PcDailyReportDto>> GetDailyAsync(PcDailyQuery query, CancellationToken cancellationToken)
        {
            if (query == null) return Bad<PcDailyReportDto>(PcText.InvalidData);
            DateTime date = (query.Date ?? DateTime.Today).Date;
            string unit = Clean(query.Unit)?.ToUpperInvariant() ?? "ALL";
            PcDailyReport existing = await _uow.Set<PcDailyReport>().AsNoTracking().Include(r => r.Lines).AsSplitQuery().FirstOrDefaultAsync(r => r.ContractId == query.ContractId && r.Unit == unit && r.ReportDate == date && !r.Deleted, cancellationToken);
            if (existing != null) return ResponseDtoHelper.Ok(await DailyDtoAsync(existing, cancellationToken));
            // pre-fill from what the modules already know (the unit manager confirms or adds the exceptions)
            IQueryable<PcActivity> q = _uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == query.ContractId && !a.Deleted && a.Status != PcActivityStatus.Done && a.DurationDays > 0
                && (a.ForecastStart ?? a.EarlyStart) != null && (a.ForecastStart ?? a.EarlyStart) <= date);
            if (unit != "ALL") q = q.Where(a => a.ResponsibleUnit == unit);
            List<PcActivity> acts = await q.OrderBy(a => a.Sort).ToListAsync(cancellationToken);
            var dto = new PcDailyReportDto { ContractId = query.ContractId, Unit = unit, ReportDate = date, State = 0 };
            foreach (PcActivity a in acts)
            {
                DateTime? finish = a.ForecastFinish ?? a.EarlyFinish;
                bool late = a.IsLate || (finish.HasValue && finish.Value < date);
                dto.Lines.Add(new PcDailyLineDto
                {
                    ActivityId = a.Id, ActivityCode = a.Code, ActivityTitle = a.Title, Percent = a.PercentCache, IsLate = late,
                    EventText = a.Status == PcActivityStatus.InProgress ? "در حال انجام (از رویدادهای سیستم)" : "طبق برنامه باید شروع یا ادامه یابد",
                });
            }
            return ResponseDtoHelper.Ok(dto);
        }

        public async Task<ResponseDto<PcDailyReportDto>> SaveDailyAsync(PcDailyReportDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcDailyReportDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.UnitManagers + ",PMO")) return Bad<PcDailyReportDto>(PcText.NotAllowed);
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken)) return PcText.NotFound<PcDailyReportDto>(PcText.ContractNotFound);
            string unit = Clean(dto.Unit)?.ToUpperInvariant() ?? "ALL";
            DateTime date = (dto.ReportDate == default ? DateTime.Today : dto.ReportDate).Date;
            List<int> actIds = (dto.Lines ?? new List<PcDailyLineDto>()).Select(l => l.ActivityId).Distinct().ToList();
            if (actIds.Count != (dto.Lines ?? new List<PcDailyLineDto>()).Count) return Bad<PcDailyReportDto>("فعالیت تکراری در گزارش روزانه است.");
            if (await _uow.Set<PcActivity>().CountAsync(a => actIds.Contains(a.Id) && a.ContractId == dto.ContractId && !a.Deleted, cancellationToken) != actIds.Count) return Bad<PcDailyReportDto>("فعالیتی از این قرارداد نیست.");
            DateTimeOffset now = DateTimeOffset.Now;
            PcDailyReport r = await _uow.Set<PcDailyReport>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.ContractId == dto.ContractId && x.Unit == unit && x.ReportDate == date && !x.Deleted, cancellationToken);
            if (r == null)
            {
                r = new PcDailyReport { ContractId = dto.ContractId, Unit = unit, ReportDate = date, CreateDate = now, CreatorUserId = Me, Lines = new List<PcDailyReportLine>() };
                _uow.Set<PcDailyReport>().Add(r);
            }
            else
            {
                if (r.State == 1) return Bad<PcDailyReportDto>("گزارش تاییدشده تغییر نمی‌کند.");
                _uow.Set<PcDailyReportLine>().RemoveRange(r.Lines);
                r.Lines = new List<PcDailyReportLine>();
            }
            foreach (PcDailyLineDto l in dto.Lines ?? new List<PcDailyLineDto>())
                r.Lines.Add(new PcDailyReportLine
                {
                    ActivityId = l.ActivityId, EventText = Clean(l.EventText), Percent = Math.Min(100m, Math.Max(0m, l.Percent)), IsLate = l.IsLate, DelayReasonCode = Clean(l.DelayReasonCode), DelayReason = Clean(l.DelayReason),
                    PreventiveAction = Clean(l.PreventiveAction), CorrectiveAction = Clean(l.CorrectiveAction),
                });
            r.ModifyDate = now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await DailyDtoAsync(r, cancellationToken));
        }

        public async Task<ResponseDto<PcDailyReportDto>> ConfirmDailyAsync(int id, CancellationToken cancellationToken)
        {
            PcDailyReport r = await _uow.Set<PcDailyReport>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (r == null) return PcText.NotFound<PcDailyReportDto>("گزارش روزانه پیدا نشد.");
            if (r.State == 1) return Bad<PcDailyReportDto>("گزارش قبلاً تایید شده است.");
            if (r.Unit != "ALL" ? !_user.IsInRole(r.Unit + ",PMO,PLANNING") : !_user.IsInRole(ProjectControlRoles.UnitManagers + ",PMO")) return Bad<PcDailyReportDto>(PcText.NotAllowed + " (گزارش را مدیر همان واحد تایید می‌کند)");
            PcDailyReportLine bad = r.Lines.FirstOrDefault(l => l.IsLate && (Clean(l.DelayReasonCode) == null || Clean(l.DelayReason) == null || Clean(l.CorrectiveAction) == null));
            if (bad != null) return Bad<PcDailyReportDto>("برای فعالیت دارای تاخیر، کد و شرح دلیل تاخیر و اقدام اصلاحی لازم است.");
            r.State = 1; r.ConfirmedByUserId = Me; r.ConfirmedAt = DateTimeOffset.Now; r.ModifyDate = r.ConfirmedAt.Value; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await DailyDtoAsync(r, cancellationToken));
        }
    }
}
