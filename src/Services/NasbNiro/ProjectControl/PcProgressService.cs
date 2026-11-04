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
using Services.NasbNiro.Office;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.ProjectControl
{
    public class PcApprovedProgress
    {
        public int ActivityId { get; set; }
        public decimal Percent { get; set; }
        public DateTime? ActualStart { get; set; }
        public DateTime? ActualFinish { get; set; }
        public string Comment { get; set; }
        public int SubmittedByUserId { get; set; }
        public int ApprovedByUserId { get; set; }
        public decimal Cost { get; set; }
        public int CostKind { get; set; }
        public string CostNote { get; set; }

        /// <summary>The person who last chose the percent (the submitter, or the approver who corrected it at an intermediate level): the manual proposal records him as the proposer.</summary>
        public int? ProposerUserId { get; set; }

        /// <summary>The person who last chose the cost amount: the maker of the cost entry. The second finance person must be someone else than him and than everyone who decided the report.</summary>
        public int? CostSetByUserId { get; set; }

        public int SubmissionId { get; set; }
        public string SubmissionNumber { get; set; }
    }

    public class PcAppliedProgress
    {
        public PcManualProgress Manual { get; set; }
        public CostEntry Cost { get; set; }
    }

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

        /// <summary>R1c (M5): the second FINANCIAL person checks a cost entry that was written by hand; only then does it count as actual cost.</summary>
        Task<ResponseDto<PcCostEntryDto>> CheckCostAsync(PcCostCheckDto dto, CancellationToken cancellationToken);

        /// <summary>R1c (H4): a snapshot for a data date in the past needs a request of planning, approved by the PMO deputy.</summary>
        Task<ResponseDto<PagedList<PcPastSnapshotDto>>> GetPastSnapshotRequestsAsync(PcPastSnapshotQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcPastSnapshotDto>> RequestPastSnapshotAsync(PcPastSnapshotRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcPastSnapshotDto>> DecidePastSnapshotAsync(PcPastSnapshotDecisionDto dto, CancellationToken cancellationToken);

        /// <summary>
        /// R1c (H4, M4): the nightly run of the engine for every contract with a current baseline: writes the progress events of the day (so the earned value of a past data date can be read back),
        /// and writes the frozen planned-value rows of a baseline that has none (a baseline of before R1c). Returns the number of contracts done.
        /// </summary>
        Task<int> NightlyAsync(CancellationToken cancellationToken);

        Task<ResponseDto<PcSnapshotDto>> TakeSnapshotAsync(PcSnapshotRequestDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcSnapshotRunResultDto>> RunDueSnapshotsAsync(PcSnapshotRunDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcSnapshotDto>>> GetSnapshotsAsync(PcSnapshotQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcSnapshotDto>> GetSnapshotAsync(int id, CancellationToken cancellationToken);

        Task<ResponseDto<PcDailyReportDto>> GetDailyAsync(PcDailyQuery query, CancellationToken cancellationToken);
        Task<ResponseDto<PcDailyReportDto>> SaveDailyAsync(PcDailyReportDto dto, CancellationToken cancellationToken);
        Task<ResponseDto<PcDailyReportDto>> ConfirmDailyAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PcDailyReportDto>> GetDailyByIdAsync(int id, CancellationToken cancellationToken);
        Task<ResponseDto<PagedList<PcDailyListDto>>> GetDailyListAsync(PcDailyListQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// P-B7a: writes a progress value that the approval chain of the progress cartable approved. It uses the same dated-event path as the manual proposal (all date rules apply, the method of the activity is not changed,
        /// so the plan lock is not touched) and adds an unchecked manual cost entry when the report carries a cost. Nothing is saved; the caller saves.
        /// </summary>
        Task<ResponseDto<PcAppliedProgress>> ApplyApprovedProgressAsync(PcApprovedProgress input, CancellationToken cancellationToken);
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

            /// <summary>R1c (H1): the weights are the ones frozen with the current baseline (an activity that is not in the baseline weighs nothing).</summary>
            public bool WeightsFromBaseline;
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

            // P-B7a: tasks linked to the activity: weighted by estimated hours, only the percent the approval cartable approved, never a personal task, a rejected task does not count
            List<int> taskActIds = calc.Activities.Where(a => a.ProgressMethod == PcProgressMethod.Tasks).Select(a => a.Id).ToList();
            var taskRows = taskActIds.Count == 0 ? null : (await _uow.Set<Entities.Model.NasbNiro.Tasks.TmTask>().AsNoTracking().Where(t => !t.Deleted && t.ActivityId != null && taskActIds.Contains(t.ActivityId.Value) && t.Kind != Entities.Model.NasbNiro.Tasks.TmTaskKind.Personal && t.Status != Entities.Model.NasbNiro.Tasks.TmTaskStatus.Rejected)
                .Select(t => new { ActivityId = t.ActivityId.Value, t.EstimatedHours, Percent = t.ApprovedPercent, t.Status, t.ClosedAt }).ToListAsync(cancellationToken)).GroupBy(t => t.ActivityId).ToDictionary(g => g.Key, g => g.ToList());

            // engineering: the state of the register lines
            List<int> engOrders = calc.Activities.Where(a => a.ProgressMethod == PcProgressMethod.EngineeringIssue && a.WorkOrderId.HasValue).Select(a => a.WorkOrderId.Value).Distinct().ToList();
            var deliverables = await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().Where(d => !d.Deleted && d.Required && d.WorkOrderId != null && engOrders.Contains(d.WorkOrderId.Value))
                .Select(d => new { WorkOrderId = d.WorkOrderId.Value, d.ClientSubmittal, d.IssueState, d.ApprovedAt, d.ClientResponseDate, d.LastTransmittalDate }).ToListAsync(cancellationToken);

            // supply and services
            List<int> poLineIds = calc.Activities.Where(a => (a.ProgressMethod == PcProgressMethod.SupplyReceipt || a.ProgressMethod == PcProgressMethod.ServiceDocs) && a.PurchaseOrderLineId.HasValue).Select(a => a.PurchaseOrderLineId.Value).Distinct().ToList();
            var supply = await SupplyAsync(poLineIds, settings, cancellationToken);

            // PB2: the procured percent of the material balance (what is not short and not only asked for: stock issued or reserved, and goods ordered), from the latest run of the project
            Dictionary<int, decimal> balanceRows = new Dictionary<int, decimal>();
            List<int> balActIds = calc.Activities.Where(a => a.ProgressMethod == PcProgressMethod.MaterialBalance).Select(a => a.Id).ToList();
            if (balActIds.Count > 0)
            {
                var runs = await _uow.Set<Entities.Model.NasbNiro.ProjectBalance.ProjectMaterialBalance>().AsNoTracking().Where(r => !r.Deleted).Select(r => new { r.Id, r.BaselineId, r.RunNumber }).ToListAsync(cancellationToken);
                List<int> latest = runs.GroupBy(r => r.BaselineId).Select(g => g.OrderByDescending(r => r.RunNumber).First().Id).ToList();
                var rows = await _uow.Set<Entities.Model.NasbNiro.ProjectBalance.ProjectMaterialBalanceLine>().AsNoTracking().Where(l => latest.Contains(l.BalanceId) && l.ActivityId != null && balActIds.Contains(l.ActivityId.Value) && l.RequiredQty > 0)
                    .Select(l => new { ActivityId = l.ActivityId.Value, l.RequiredQty, l.ShortQty, l.OnOrderPrQty }).ToListAsync(cancellationToken);
                foreach (var g in rows.GroupBy(r => r.ActivityId))
                {
                    decimal need = g.Sum(r => r.RequiredQty);
                    decimal covered = g.Sum(r => Math.Min(r.RequiredQty, Math.Max(0m, r.RequiredQty - r.ShortQty - r.OnOrderPrQty)));
                    balanceRows[g.Key] = need <= 0 ? 0m : Math.Round(covered / need * 100m, 2, MidpointRounding.AwayFromZero);
                }
            }

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
                    case PcProgressMethod.Tasks:
                        if (taskRows != null && taskRows.TryGetValue(a.Id, out var mineTasks))
                        {
                            decimal? rolled = global::Services.NasbNiro.Tasks.TaskRules.RollUpPercent(mineTasks.Select(t => (t.EstimatedHours, t.Percent, t.Status)));
                            if (rolled.HasValue)
                            {
                                pct = rolled.Value;
                                // the day of the fact is today (the day the approved value is seen), not the day the first task began: progress reached now is not written into the past
                                if (pct >= 100m) finish = mineTasks.Where(t => t.ClosedAt.HasValue).Select(t => (DateTime?)t.ClosedAt.Value.LocalDateTime.Date).Max();
                            }
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
                    case PcProgressMethod.MaterialBalance:
                        if (balanceRows.TryGetValue(a.Id, out decimal procured)) pct = procured;
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

            // hierarchical weights: R1c (H1) the weights frozen with the current baseline when it has them (progress that triggers a payment follows the approved plan, not the live tree), else the live tree
            Dictionary<int, decimal> frozen = await BaselineWeightsAsync(contractId, cancellationToken);
            if (frozen != null) { calc.Weight = frozen; calc.WeightsFromBaseline = true; }
            else
            {
                List<PcWbsNode> nodes = await _uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == contractId && !n.Deleted).ToListAsync(cancellationToken);
                calc.Weight = EffectiveWeights(nodes, calc.Activities);
            }
            return calc;
        }

        /// <summary>R1c (H1): the share of every activity of the current baseline in the whole project, as frozen with it; null when there is no current baseline or it carries no weights (a baseline of before R1c).</summary>
        private async Task<Dictionary<int, decimal>> BaselineWeightsAsync(int contractId, CancellationToken cancellationToken)
        {
            int? baselineId = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => t.ContractId == contractId && !t.Deleted).Select(t => t.CurrentBaselineId).FirstOrDefaultAsync(cancellationToken);
            if (!baselineId.HasValue) return null;
            var rows = await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(r => r.BaselineId == baselineId.Value).Select(r => new { r.ActivityId, r.EffectiveWeight }).ToListAsync(cancellationToken);
            if (rows.Count == 0 || rows.Any(r => !r.EffectiveWeight.HasValue)) return null;
            return rows.ToDictionary(r => r.ActivityId, r => r.EffectiveWeight.Value);
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
            if (total <= 0m) return c.WeightsFromBaseline ? 0m : PcText.Round2(acts.Average(a => c.Percent[a.Id]));
            return PcText.Round2(acts.Sum(a => (c.Weight.TryGetValue(a.Id, out decimal w) ? w : 0m) * c.Percent[a.Id]) / total);
        }

        public Task<List<PcActivity>> ApplyAsync(int contractId, CancellationToken cancellationToken) => ApplyAsync(contractId, DateTime.Today, cancellationToken);

        /// <param name="seenOn">The day the changes are seen: the latest day an event of this run may carry (the nightly run before the working day passes the previous day).</param>
        private async Task<List<PcActivity>> ApplyAsync(int contractId, DateTime seenOn, CancellationToken cancellationToken)
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
            // R1c (H4): every change of the percent is written with the day it was seen, so earned value of a later data date can be read back as it was known on that day
            // R1c (C9): ... and an event carries the day of the fact behind it (receipt, QC result, approval, release, shipment), not the day the engine happened to run
            var factDates = new Dictionary<int, DateTime>();
            foreach (PcActivity a in c.Activities)
            {
                DateTime? fact = c.FinishDate[a.Id] ?? c.StartDate[a.Id];
                if (fact.HasValue) factDates[a.Id] = fact.Value.Date;
            }
            await PcProgressEvents.RecordAsync(_uow, contractId, c.Percent, seenOn.Date, Me, PcProgressEvents.SourceEngine, cancellationToken, factDates);
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
            decimal PlannedOf(PcActivity a) => plan.Items.TryGetValue(a.Id, out PcCurveItem it) ? plan.Fraction(it, asOf) * 100m : 0m;
            decimal Weighted(IEnumerable<PcActivity> list, Func<PcActivity, decimal> value)
            {
                List<PcActivity> l = list.ToList();
                decimal total = l.Sum(a => c.Weight.TryGetValue(a.Id, out decimal w) ? w : 0m);
                if (l.Count == 0) return 0m;
                if (total <= 0m) return c.WeightsFromBaseline ? 0m : PcText.Round2(l.Average(value));
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
            Id = m.Id, ActivityId = m.ActivityId, ActivityCode = m.Activity?.Code, ActivityTitle = m.Activity?.Title, Percent = m.Percent, Comment = m.Comment, ProgressDate = m.ProgressDate, ProposedByUserId = m.ProposedByUserId,
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
            // R1c (H4): the day the progress was reached is dated; the future is refused
            DateTime progressDate = (dto.ProgressDate ?? DateTime.Today).Date;
            string dateProblem = await ProgressDateProblemAsync(a.ContractId, a.Id, progressDate, cancellationToken);
            if (dateProblem != null) return Bad<PcManualProgressDto>(dateProblem);
            var row = new PcManualProgress { ActivityId = a.Id, Percent = PcText.Round2(dto.Percent), Comment = Clean(dto.Comment), ProgressDate = progressDate, ProposedByUserId = Me, ProposedAt = DateTimeOffset.Now, State = 0 };
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
            if (dto.Approve)
            {
                // R1c (H4): the approved percent is a dated event. It counts from the day it was reached; when a weekly snapshot was taken for that day or later in the meantime, from today
                DateTime day = (m.ProgressDate ?? DateTime.Today).Date;
                if (await ProgressDateProblemAsync(m.Activity.ContractId, m.ActivityId, day, cancellationToken) != null) day = DateTime.Today;
                m.ProgressDate = day;
                PcProgressEvents.Add(_uow, m.Activity.ContractId, m.ActivityId, day, m.Percent, Me, PcProgressEvents.SourceManual);
            }
            await _uow.SaveChangesAsync(cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { m.ProposedByUserId, m.DecidedByUserId }, cancellationToken);
            return ResponseDtoHelper.Ok(ToDto(m, names));
        }

        /// <summary>
        /// R1c (H4): the problem with the day a progress was reached, or null. It is not in the future; and unless it is today, it is after the data date of the last weekly snapshot
        /// (that week was reported and does not change) and not before the previous approved progress of the same activity.
        /// </summary>
        private async Task<string> ProgressDateProblemAsync(int contractId, int activityId, DateTime day, CancellationToken cancellationToken)
        {
            DateTime today = DateTime.Today;
            day = day.Date;
            if (day > today) return "تاریخ پیشرفت در آینده است؛ فقط پیشرفتی ثبت می‌شود که تا امروز انجام شده است.";
            if (day == today) return null;
            DateTime? lastSnapshot = await _uow.Set<PcProgressSnapshot>().AsNoTracking().Where(s => s.ContractId == contractId).MaxAsync(s => (DateTime?)s.DataDate, cancellationToken);
            if (lastSnapshot.HasValue && day <= lastSnapshot.Value.Date)
                return "تاریخ پیشرفت باید پس از تاریخ داده آخرین عکس هفتگی (" + global::Services.NasbNiro.Finance.FiscalCalendars.Text(lastSnapshot.Value) + ") باشد؛ آن هفته گزارش شده و تغییر نمی‌کند. پیشرفت را با تاریخ امروز ثبت کنید.";
            DateTime? previous = (await _uow.Set<PcManualProgress>().AsNoTracking().Where(m => m.ActivityId == activityId && m.State == 1).Select(m => new { m.ProgressDate, m.DecidedAt }).ToListAsync(cancellationToken))
                .Select(m => m.ProgressDate ?? m.DecidedAt?.Date).DefaultIfEmpty().Max();
            if (previous.HasValue && day < previous.Value.Date) return "تاریخ پیشرفت نمی‌تواند پیش از پیشرفت تاییدشده قبلی همین فعالیت باشد.";
            return null;
        }

        // ============================================================================ plan, earned value

        private sealed class PcPlanSet
        {
            public bool FromBaseline;
            public int? BaselineId;
            public decimal Bac;
            public decimal ManagementReserve;
            public DateTime? Start;
            public DateTime? Finish;

            /// <summary>The calendar of the contract as it is today; used only to count the days after the planned finish (the frozen rows stop there).</summary>
            public PcWorkCalendar Calendar;
            public PcAccrualMode Mode;

            /// <summary>R1c (M4): the planned-value curve; read from the rows frozen at the approval of the baseline when <see cref="Frozen"/>, else computed with today's calendars.</summary>
            public PcCurve Curve = new PcCurve();
            public bool Frozen;
            public Dictionary<int, PcCurveItem> Items = new Dictionary<int, PcCurveItem>();

            public decimal Fraction(PcCurveItem it, DateTime day) => Curve.Fraction(it, day);

            /// <summary>The planned value of the whole plan at the end of the day.</summary>
            public decimal PvOn(DateTime day) => Curve.Pv(day);
        }

        private async Task<PcPlanSet> LoadPlanAsync(int contractId, List<PcActivity> acts, CancellationToken cancellationToken)
        {
            PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
            var plan = new PcPlanSet { Mode = terms?.AccrualMode ?? PcAccrualMode.Linear, Calendar = await PcContext.CalendarForContractAsync(_uow, terms, cancellationToken) };
            List<PcBaselineCostPeriod> periods = null;
            if (terms?.CurrentBaselineId != null)
            {
                PcBaseline b = await _uow.Set<PcBaseline>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == terms.CurrentBaselineId.Value && !x.Deleted, cancellationToken);
                if (b != null)
                {
                    plan.FromBaseline = true; plan.BaselineId = b.Id; plan.Bac = b.Bac; plan.ManagementReserve = b.ManagementReserve;
                    List<PcBaselineActivity> rows = await _uow.Set<PcBaselineActivity>().AsNoTracking().Where(x => x.BaselineId == b.Id).ToListAsync(cancellationToken);
                    foreach (PcCurveItem item in PcFrozenPlan.ItemsOf(rows)) plan.Items[item.ActivityId] = item;
                    periods = await _uow.Set<PcBaselineCostPeriod>().AsNoTracking().Where(x => x.BaselineId == b.Id).ToListAsync(cancellationToken);
                }
            }
            if (!plan.FromBaseline)
            {
                plan.ManagementReserve = terms?.ManagementReserve ?? 0m;
                foreach (PcActivity a in acts.Where(a => a.EarlyStart.HasValue && a.EarlyFinish.HasValue))
                    plan.Items[a.Id] = new PcCurveItem { ActivityId = a.Id, Start = a.EarlyStart.Value.Date, Finish = a.EarlyFinish.Value.Date, Budget = a.BudgetAtCompletion, Duration = a.DurationDays, CalendarKey = a.CalendarId ?? PcCurve.ContractCalendar };
                plan.Bac = plan.Items.Values.Sum(i => i.Budget);
            }
            if (plan.Items.Count > 0)
            {
                plan.Start = plan.Items.Values.Min(i => i.Start); plan.Finish = plan.Items.Values.Max(i => i.Finish);
                if (periods != null && periods.Count > 0)
                {
                    plan.Curve = PcCurve.FromRows(periods, plan.Mode);
                    plan.Frozen = true;
                }
                else
                {
                    var items = plan.Items.Values.ToList();
                    plan.Curve = PcCurve.Build(items, await PcFrozenPlan.CalendarsAsync(_uow, terms, items.Select(i => i.CalendarKey), cancellationToken), plan.Mode);
                }
            }
            return plan;
        }

        /// <summary>Entries written by hand in project control; they count only after a second person checked them (M5).</summary>
        internal const string ManualCostSource = "ProjectControl";

        /// <summary>
        /// R1c (M5): the cost entries that count as the actual cost of a contract up to the end of the day: the after-sales costs (service orders and their monthly labour) are not project cost,
        /// and an entry written by hand in project control counts only after a second FINANCIAL person checked it.
        /// </summary>
        private IQueryable<CostEntry> ActualCosts(int contractId, DateTime asOf)
        {
            DateTime end = asOf.Date.AddDays(1);
            return _uow.Set<CostEntry>().AsNoTracking().Where(e => e.EntryDate < end && (e.ContractId == contractId || (e.ContractId == null && e.WorkOrder != null && e.WorkOrder.ContractId == contractId))
                && e.SourceType != global::Services.NasbNiro.AfterSales.SvCostSource.Order && e.SourceType != global::Services.NasbNiro.AfterSales.SvLabourMonth.SourceType
                && (e.SourceType != ManualCostSource || _uow.Set<PcCostCheck>().Any(c => c.CostEntryId == e.Id)));
        }

        private async Task<decimal> ActualCostAsync(int contractId, DateTime asOf, CancellationToken cancellationToken) =>
            await ActualCosts(contractId, asOf).SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        public async Task<PcEvmDto> ComputeEvmAsync(int contractId, DateTime asOf, CancellationToken cancellationToken)
        {
            asOf = asOf.Date;
            Calc c = await ComputeAsync(contractId, false, asOf, cancellationToken);
            bool fromEvents = false;
            string note = null;
            if (asOf < DateTime.Today)
            {
                // R1c (H4): the percents of a past data date are the ones known on that day (the dated progress events), not the module data of today
                fromEvents = true;
                if (!await UseEventsAsync(c, contractId, asOf, cancellationToken)) note = "برای این قرارداد هنوز رویداد پیشرفت ثبت نشده است؛ پیشرفت تاریخ‌های گذشته در دسترس نیست (اجرای شبانه یا محاسبه پیشرفت امروز، رویدادها را می‌سازد).";
            }
            PcEvmDto dto = await EvmFromAsync(contractId, asOf, c, cancellationToken);
            dto.FromEvents = fromEvents;
            if (note != null) dto.Note = dto.Note == null ? note : dto.Note + " " + note;
            return dto;
        }

        private async Task<PcEvmDto> EvmFromAsync(int contractId, DateTime asOf, Calc c, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> settings = await PcSettingKeys.LoadAsync(_uow, cancellationToken);
            PcPlanSet plan = await LoadPlanAsync(contractId, c.Activities, cancellationToken);
            var dto = new PcEvmDto { ContractId = contractId, AsOf = asOf, BaselineId = plan.BaselineId, FromBaseline = plan.FromBaseline, Bac = plan.Bac, ManagementReserve = plan.ManagementReserve, SpiStatus = "none", CpiStatus = "none", PvFrozen = plan.Frozen };
            dto.ProductionForecastFinish = c.Activities.Where(a => a.ProductionForecastFinish.HasValue).Select(a => a.ProductionForecastFinish).DefaultIfEmpty().Max();
            if (plan.Items.Count == 0) { dto.Note = "مبنای زمان‌بندی یا برنامه محاسبه‌شده‌ای وجود ندارد."; dto.Ac = await ActualCostAsync(contractId, asOf, cancellationToken); return dto; }
            decimal pv = plan.PvOn(asOf), ev = 0m;
            foreach (PcCurveItem it in plan.Items.Values)
                ev += c.Percent.TryGetValue(it.ActivityId, out decimal p) ? it.Budget * p / 100m : 0m;
            dto.Pv = PcText.Round2(pv); dto.Ev = PcText.Round2(ev); dto.Ac = PcText.Round2(await ActualCostAsync(contractId, asOf, cancellationToken));
            dto.Cv = dto.Ev - dto.Ac; dto.Sv = dto.Ev - dto.Pv;
            if (dto.Ac > 0m) dto.Cpi = PcText.Round4(dto.Ev / dto.Ac);
            if (dto.Pv > 0m) dto.Spi = PcText.Round4(dto.Ev / dto.Pv);

            // earned schedule (working days of the contract calendar)
            int last = Math.Max(0, plan.Curve.ContractWorkingDays - 1);
            int at = plan.Curve.ActualDays(asOf, plan.Calendar);
            decimal es = 0m;
            if (dto.Ev > 0m)
            {
                decimal prev = 0m;
                es = last + 1;
                for (int k = 0; k <= last; k++)
                {
                    decimal cur = plan.Curve.PvAtWorkingDay(k);
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
            decimal Weighted(Func<PcActivity, decimal> value) => totalW <= 0m ? (c.Activities.Count == 0 || c.WeightsFromBaseline ? 0m : c.Activities.Average(value)) : c.Activities.Sum(a => (c.Weight.TryGetValue(a.Id, out decimal w) ? w : 0m) * value(a)) / totalW;
            dto.PhysicalPercent = PcText.Round2(Weighted(a => c.Percent[a.Id]));
            dto.PlannedPercent = PcText.Round2(Weighted(a => plan.Items.TryGetValue(a.Id, out PcCurveItem it) ? plan.Fraction(it, asOf) * 100m : 0m));
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
                PcProgressSnapshot snap = snaps.LastOrDefault(s => s.DataDate <= point);
                points.Add(new PcCashFlowPointDto { Date = point, PlannedCumulative = PcText.Round2(plan.PvOn(point)), ActualCostCumulative = PcText.Round2(await ActualCostAsync(contractId, point, cancellationToken)), EarnedValue = snap?.Ev });
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
            // R1c (H4): a cost is booked for a day that has happened; the actual cost of a data date cannot be filled in ahead of time
            if (dto.EntryDate != default && dto.EntryDate.Date > DateTime.Today) return Bad<PcCostEntryDto>("تاریخ هزینه در آینده است؛ هزینه‌ای ثبت می‌شود که تا امروز انجام شده است.");
            // H5: the entries made before the second check existed are marked as checked once, before the first entry of the new rule is written
            await PcR1Upgrade.EnsureAsync(_uow, cancellationToken);
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
            return ResponseDtoHelper.Ok(new PcCostEntryDto
            {
                Id = e.Id, ContractId = e.ContractId, WorkOrderId = e.WorkOrderId, WbsNodeId = e.WbsNodeId, Kind = e.Kind, EntryDate = e.EntryDate, Amount = e.Amount, SourceType = e.SourceType, SourceNumber = e.SourceNumber, Description = e.Description,
                NeedsCheck = true, Checked = false, CreatedByUserId = e.CreatedByUserId, ExcludedFromActual = true,
            });
        }

        /// <summary>R1c (M5): the second FINANCIAL person checks a manual cost entry; only then it is part of the actual cost. The person who wrote the entry cannot check it.</summary>
        public async Task<ResponseDto<PcCostEntryDto>> CheckCostAsync(PcCostCheckDto dto, CancellationToken cancellationToken)
        {
            if (dto == null || dto.CostEntryId <= 0) return Bad<PcCostEntryDto>(PcText.InvalidData);
            if (!_user.IsInRole("FINANCIAL,FINANCEMANAGER")) return Bad<PcCostEntryDto>(PcText.NotAllowed + " (بررسی هزینه دستی با کارشناس یا مدیر مالی است)");
            CostEntry e = await _uow.Set<CostEntry>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.CostEntryId, cancellationToken);
            if (e == null) return PcText.NotFound<PcCostEntryDto>("ردیف هزینه پیدا نشد.");
            if (e.SourceType != ManualCostSource) return Bad<PcCostEntryDto>("فقط هزینه‌ای که با دست در کنترل پروژه ثبت شده بررسی دوم می‌خواهد؛ هزینه‌های سیستمی از ماژول خودشان می‌آیند.");
            if (e.CreatedByUserId == Me) return Bad<PcCostEntryDto>(PcText.SameUser + " (ثبت‌کننده هزینه نمی‌تواند آن را بررسی کند)");
            // a cost that came from a progress report: nobody who touched the amount (the submitter, the person who set it, every approver who decided the report) checks it
            int me = Me;
            if (await _uow.Set<PcProgressSubmission>().AsNoTracking().AnyAsync(sub => sub.CostEntryId == e.Id && (sub.SubmittedByUserId == me || sub.CreatorUserId == me || sub.CostSetByUserId == me || sub.Steps.Any(st => st.DecidedByUserId == me)), cancellationToken))
                return Bad<PcCostEntryDto>(PcText.SameUser + " (کسی که گزارش را ارسال کرده، مبلغ را نوشته یا در زنجیره تایید تصمیم گرفته نمی‌تواند هزینه را بررسی کند)");
            if (await _uow.Set<PcCostCheck>().AnyAsync(c => c.CostEntryId == e.Id, cancellationToken)) return Bad<PcCostEntryDto>("این هزینه قبلاً بررسی شده است.");
            var check = new PcCostCheck { CostEntryId = e.Id, CheckedByUserId = Me, CheckedAt = DateTimeOffset.Now, Note = Clean(dto.Note) };
            _uow.Set<PcCostCheck>().Add(check);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcCostEntryDto>("این هزینه هم‌زمان توسط شخص دیگری بررسی شد."); }
            return ResponseDtoHelper.Ok(new PcCostEntryDto
            {
                Id = e.Id, ContractId = e.ContractId, WorkOrderId = e.WorkOrderId, WbsNodeId = e.WbsNodeId, Kind = e.Kind, EntryDate = e.EntryDate, Amount = e.Amount, SourceType = e.SourceType, SourceNumber = e.SourceNumber, Description = e.Description,
                NeedsCheck = true, Checked = true, CheckedByUserId = check.CheckedByUserId, CheckedAt = check.CheckedAt, CreatedByUserId = e.CreatedByUserId, ExcludedFromActual = false,
            });
        }

        public async Task<ResponseDto<PagedList<PcCostEntryDto>>> GetCostsAsync(PcContractQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcContractQuery();
            if (!query.ContractId.HasValue) return Bad<PagedList<PcCostEntryDto>>("قرارداد را مشخص کنید.");
            int id = query.ContractId.Value;
            IQueryable<PcCostEntryDto> projection = _uow.Set<CostEntry>().AsNoTracking().Where(e => e.ContractId == id || (e.ContractId == null && e.WorkOrder != null && e.WorkOrder.ContractId == id))
                .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id).Select(e => new PcCostEntryDto
                {
                    NeedsCheck = e.SourceType == ManualCostSource, Checked = _uow.Set<PcCostCheck>().Any(c => c.CostEntryId == e.Id), CheckedAt = _uow.Set<PcCostCheck>().Where(c => c.CostEntryId == e.Id).Select(c => (DateTimeOffset?)c.CheckedAt).FirstOrDefault(),
                    CheckedByUserId = _uow.Set<PcCostCheck>().Where(c => c.CostEntryId == e.Id).Select(c => (int?)c.CheckedByUserId).FirstOrDefault(), CreatedByUserId = e.CreatedByUserId,
                    ExcludedFromActual = e.SourceType == global::Services.NasbNiro.AfterSales.SvCostSource.Order || e.SourceType == global::Services.NasbNiro.AfterSales.SvLabourMonth.SourceType
                        || (e.SourceType == ManualCostSource && !_uow.Set<PcCostCheck>().Any(c => c.CostEntryId == e.Id)),
                    Id = e.Id, ContractId = e.ContractId, WorkOrderId = e.WorkOrderId, WbsNodeId = e.WbsNodeId, Kind = e.Kind, EntryDate = e.EntryDate, Amount = e.Amount, SourceType = e.SourceType, SourceNumber = e.SourceNumber, Description = e.Description,
                    LabourHidden = e.Kind == CostKind.Labour && e.LabourProtected,
                });
            PagedList<PcCostEntryDto> page = await PagedList<PcCostEntryDto>.CreateAsync(projection, query.PageNumber, query.PageSize, cancellationToken);
            // R1p D1: a protected labour entry (it could show one person's pay) carries its amount only for PAYROLL, FINANCEMANAGER and CEO
            bool seesPayroll = Production.MfPrivacy.SeesPayroll(_user);
            foreach (PcCostEntryDto row in page)
            {
                if (seesPayroll) row.LabourHidden = false;
                else if (row.LabourHidden) row.Amount = null;
            }
            return ResponseDtoHelper.Ok(page);
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

        /// <summary>
        /// R1c (H4): for a data date before today the percents are the ones known on that day (the dated progress events). Returns false when the contract has no event at all
        /// (the percents are then zero and the figures of a past date are not available).
        /// </summary>
        private async Task<bool> UseEventsAsync(Calc c, int contractId, DateTime asOf, CancellationToken cancellationToken)
        {
            List<PcProgressEvent> events = await PcProgressEvents.ListAsync(_uow, contractId, cancellationToken);
            Dictionary<int, decimal> known = PcProgressEvents.AsOf(events, asOf);
            foreach (PcActivity a in c.Activities) c.Percent[a.Id] = known.TryGetValue(a.Id, out decimal p) ? p : 0m;
            return events.Count > 0;
        }

        private async Task<(PcProgressSnapshot Snapshot, string Error)> BuildSnapshotAsync(int contractId, DateTime dataDate, int userId, CancellationToken cancellationToken)
        {
            if (await _uow.Set<PcProgressSnapshot>().AnyAsync(s => s.ContractId == contractId && s.DataDate == dataDate, cancellationToken)) return (null, "برای این تاریخ داده، عکس هفتگی قبلاً گرفته شده و تغییر نمی‌کند.");
            // R1c (H4): the snapshots are a chain in time; a new one is never dated before the previous one
            DateTime? previous = await _uow.Set<PcProgressSnapshot>().AsNoTracking().Where(s => s.ContractId == contractId).MaxAsync(s => (DateTime?)s.DataDate, cancellationToken);
            if (previous.HasValue && dataDate < previous.Value.Date)
                return (null, "تاریخ داده نمی‌تواند پیش از تاریخ داده آخرین عکس هفتگی (" + global::Services.NasbNiro.Finance.FiscalCalendars.Text(previous.Value) + ") باشد؛ عکس‌ها پشت‌سرهم و تغییرناپذیرند.");
            List<PcActivity> acts = await ApplyAsync(contractId, cancellationToken);
            if (acts.Count == 0) return (null, "فعالیتی برای عکس هفتگی وجود ندارد.");
            Calc c = await ComputeAsync(contractId, false, dataDate, cancellationToken);
            if (dataDate < DateTime.Today) await UseEventsAsync(c, contractId, dataDate, cancellationToken);
            PcEvmDto evm = await EvmFromAsync(contractId, dataDate, c, cancellationToken);
            PcPlanSet plan = await LoadPlanAsync(contractId, c.Activities, cancellationToken);
            var snap = new PcProgressSnapshot
            {
                ContractId = contractId, DataDate = dataDate, TakenAt = DateTimeOffset.Now, TakenByUserId = userId, BaselineId = evm.BaselineId, PhysicalPercent = evm.PhysicalPercent, PlannedPercent = evm.PlannedPercent,
                Bac = evm.Bac, Pv = evm.Pv, Ev = evm.Ev, Ac = evm.Ac, Cpi = evm.Cpi, Spi = evm.Spi, ForecastFinish = evm.ForecastFinish, Lines = new List<PcProgressSnapshotLine>(),
            };
            foreach (PcActivity a in c.Activities)
            {
                decimal pct = c.Percent[a.Id];
                decimal budget = 0m, pv = 0m, planned = 0m;
                if (plan.Items.TryGetValue(a.Id, out PcCurveItem it)) { decimal fraction = plan.Fraction(it, dataDate); budget = it.Budget; planned = fraction * 100m; pv = it.Budget * fraction; }
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
            DateTime regular = PcText.LastWeekday(DateTime.Today, terms?.DataDateWeekday ?? 3);
            DateTime dataDate = (dto.DataDate ?? regular).Date;
            if (dataDate > DateTime.Today) return Bad<PcSnapshotDto>("تاریخ داده در آینده است.");
            // R1c (H4): a data date before the current weekly data date is a back-dated snapshot: it needs the request of planning, approved by the PMO deputy (used once)
            PcSnapshotRequest approved = null;
            if (dataDate < regular)
            {
                approved = await _uow.Set<PcSnapshotRequest>().FirstOrDefaultAsync(r => r.ContractId == dto.ContractId && r.DataDate == dataDate && !r.Deleted && r.State == PcSnapshotRequestState.Approved, cancellationToken);
                if (approved == null)
                    return Bad<PcSnapshotDto>("تاریخ داده (" + global::Services.NasbNiro.Finance.FiscalCalendars.Text(dataDate) + ") پیش از آخرین تاریخ داده هفتگی (" + global::Services.NasbNiro.Finance.FiscalCalendars.Text(regular)
                        + ") است؛ عکس با تاریخ گذشته فقط پس از درخواست برنامه‌ریزی (با دلیل) و تایید معاون PMO گرفته می‌شود.");
            }
            var built = await BuildSnapshotAsync(dto.ContractId, dataDate, Me, cancellationToken);
            if (built.Error != null) return Bad<PcSnapshotDto>(built.Error);
            if (approved != null) { approved.State = PcSnapshotRequestState.Used; approved.ModifyDate = DateTimeOffset.Now; approved.ModifierUserId = Me; }
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcSnapshotDto>("برای این تاریخ داده، عکس هفتگی قبلاً گرفته شده است."); }
            if (approved != null)
            {
                approved.SnapshotId = built.Snapshot.Id;
                await _uow.SaveChangesAsync(cancellationToken);
            }
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
                // R1c (H4): the figures of a data date in the future do not exist yet
                if (dataDate > DateTime.Today) { result.Notes.Add(t.ContractId + ": تاریخ داده در آینده است."); continue; }
                // one snapshot per data date, and never one dated before the latest (the chain only grows)
                if (await _uow.Set<PcProgressSnapshot>().AnyAsync(s => s.ContractId == t.ContractId && s.DataDate >= dataDate, cancellationToken)) continue;
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

        // ---------------------------------------------------------------- snapshots with a data date in the past (H4): request by planning, approval by the PMO deputy

        private static PcPastSnapshotDto ToDto(PcSnapshotRequest r, Dictionary<int, string> names) => new PcPastSnapshotDto
        {
            Id = r.Id, ContractId = r.ContractId, DataDate = r.DataDate, Reason = r.Reason, State = r.State, RequestedByUserId = r.CreatorUserId, RequestedByName = names.Name(r.CreatorUserId), RequestedAt = r.CreateDate,
            DecidedByUserId = r.DecidedByUserId, DecidedByName = names.Name(r.DecidedByUserId), DecidedAt = r.DecidedAt, DecisionNote = r.DecisionNote, SnapshotId = r.SnapshotId,
            RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
        };

        public async Task<ResponseDto<PagedList<PcPastSnapshotDto>>> GetPastSnapshotRequestsAsync(PcPastSnapshotQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcPastSnapshotQuery();
            IQueryable<PcSnapshotRequest> q = _uow.Set<PcSnapshotRequest>().AsNoTracking().Where(r => !r.Deleted);
            if (query.ContractId.HasValue) q = q.Where(r => r.ContractId == query.ContractId.Value);
            if (query.State.HasValue) q = q.Where(r => r.State == query.State.Value);
            PagedList<PcSnapshotRequest> page = await PagedList<PcSnapshotRequest>.CreateAsync(q.OrderByDescending(r => r.Id), query.PageNumber, query.PageSize, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, page.Select(r => (int?)r.CreatorUserId).Concat(page.Select(r => r.DecidedByUserId)), cancellationToken);
            List<PcPastSnapshotDto> dtos = page.Select(r => ToDto(r, names)).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcPastSnapshotDto>)new PcPage<PcPastSnapshotDto>(dtos, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcPastSnapshotDto>> RequestPastSnapshotAsync(PcPastSnapshotRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcPastSnapshotDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Planning)) return Bad<PcPastSnapshotDto>(PcText.NotAllowed);
            string reason = Clean(dto.Reason);
            if (reason == null) return Bad<PcPastSnapshotDto>("دلیل گرفتن عکس با تاریخ گذشته را بنویسید.");
            if (!await _uow.Set<ContractEntity>().AnyAsync(x => x.Id == dto.ContractId && !x.Deleted, cancellationToken)) return PcText.NotFound<PcPastSnapshotDto>(PcText.ContractNotFound);
            PcContractTerms terms = await PcContext.TermsAsync(_uow, dto.ContractId, false, cancellationToken);
            DateTime regular = PcText.LastWeekday(DateTime.Today, terms?.DataDateWeekday ?? 3);
            DateTime dataDate = dto.DataDate.Date;
            if (dataDate > DateTime.Today) return Bad<PcPastSnapshotDto>("تاریخ داده در آینده است.");
            if (dataDate >= regular) return Bad<PcPastSnapshotDto>("این تاریخ داده گذشته نیست؛ عکس را مستقیم بگیرید (درخواست لازم نیست).");
            DateTime? previous = await _uow.Set<PcProgressSnapshot>().AsNoTracking().Where(s => s.ContractId == dto.ContractId).MaxAsync(s => (DateTime?)s.DataDate, cancellationToken);
            if (previous.HasValue && dataDate <= previous.Value.Date)
                return Bad<PcPastSnapshotDto>("تاریخ داده باید پس از تاریخ داده آخرین عکس هفتگی (" + global::Services.NasbNiro.Finance.FiscalCalendars.Text(previous.Value) + ") باشد؛ عکس‌ها پشت‌سرهم و تغییرناپذیرند.");
            if (await _uow.Set<PcSnapshotRequest>().AnyAsync(r => r.ContractId == dto.ContractId && r.DataDate == dataDate && !r.Deleted && (r.State == PcSnapshotRequestState.Requested || r.State == PcSnapshotRequestState.Approved), cancellationToken))
                return Bad<PcPastSnapshotDto>("برای این تاریخ داده درخواست باز وجود دارد.");
            DateTimeOffset now = DateTimeOffset.Now;
            var r = new PcSnapshotRequest { ContractId = dto.ContractId, DataDate = dataDate, Reason = reason, State = PcSnapshotRequestState.Requested, CreateDate = now, CreatorUserId = Me, ModifyDate = now, ModifierUserId = Me };
            _uow.Set<PcSnapshotRequest>().Add(r);
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Bad<PcPastSnapshotDto>("برای این تاریخ داده درخواست باز وجود دارد."); }
            return ResponseDtoHelper.Ok(ToDto(r, await InventoryUsers.GetNamesAsync(_uow, new int?[] { Me }, cancellationToken)));
        }

        public async Task<ResponseDto<PcPastSnapshotDto>> DecidePastSnapshotAsync(PcPastSnapshotDecisionDto dto, CancellationToken cancellationToken)
        {
            if (dto == null) return Bad<PcPastSnapshotDto>(PcText.InvalidData);
            if (!_user.IsInRole(ProjectControlRoles.Pmo)) return Bad<PcPastSnapshotDto>(PcText.NotAllowed + " (تایید عکس با تاریخ گذشته با معاون PMO است)");
            PcSnapshotRequest r = await _uow.Set<PcSnapshotRequest>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, cancellationToken);
            if (r == null) return PcText.NotFound<PcPastSnapshotDto>("درخواست پیدا نشد.");
            if (r.State != PcSnapshotRequestState.Requested) return Bad<PcPastSnapshotDto>("درباره این درخواست تصمیم گرفته شده است.");
            if (r.CreatorUserId == Me) return Bad<PcPastSnapshotDto>(PcText.SameUser);
            if (!dto.Approve && Clean(dto.Note) == null) return Bad<PcPastSnapshotDto>("دلیل رد را بنویسید.");
            if (dto.Approve && await _uow.Set<PcProgressSnapshot>().AnyAsync(s => s.ContractId == r.ContractId && s.DataDate >= r.DataDate, cancellationToken))
                return Bad<PcPastSnapshotDto>("پس از ثبت درخواست، عکسی با همین تاریخ داده یا دیرتر گرفته شده است؛ درخواست را رد کنید.");
            DateTimeOffset now = DateTimeOffset.Now;
            r.State = dto.Approve ? PcSnapshotRequestState.Approved : PcSnapshotRequestState.Rejected; r.DecidedByUserId = Me; r.DecidedAt = now; r.DecisionNote = Clean(dto.Note); r.ModifyDate = now; r.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Bad<PcPastSnapshotDto>(PcText.Concurrency); }
            return ResponseDtoHelper.Ok(ToDto(r, await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.CreatorUserId, Me }, cancellationToken)));
        }

        // ---------------------------------------------------------------- the nightly run

        /// <summary>R1c (M4): entities of a failed contract must not stay in the change tracker, or every later contract's save fails again.</summary>
        private void ClearTracker() => (_uow as Microsoft.EntityFrameworkCore.DbContext)?.ChangeTracker.Clear();

        public async Task<int> NightlyAsync(CancellationToken cancellationToken)
        {
            List<int> contractIds = await _uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.CurrentBaselineId != null).Select(t => t.ContractId).Distinct().OrderBy(id => id).ToListAsync(cancellationToken);
            int done = 0;
            foreach (int contractId in contractIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // a baseline of before R1c gets its frozen planned-value rows (from the calendars of the night it is first seen)
                    PcContractTerms terms = await PcContext.TermsAsync(_uow, contractId, false, cancellationToken);
                    PcBaseline baseline = terms?.CurrentBaselineId == null ? null : await _uow.Set<PcBaseline>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == terms.CurrentBaselineId.Value && !b.Deleted, cancellationToken);
                    if (baseline != null) await PcFrozenPlan.EnsureAsync(_uow, baseline, cancellationToken);
                    // R1c (M3): the history of an activity starts from the snapshot lines for the days before its first event (per activity, so an earlier call of the engine does not hide the history)
                    var firstEvent = (await _uow.Set<PcProgressEvent>().AsNoTracking().Where(e => e.ContractId == contractId).GroupBy(e => e.ActivityId).Select(g => new { ActivityId = g.Key, First = g.Min(e => e.EventDate) }).ToListAsync(cancellationToken))
                        .ToDictionary(x => x.ActivityId, x => x.First.Date);
                    var lines = await _uow.Set<PcProgressSnapshotLine>().AsNoTracking().Where(l => l.Snapshot.ContractId == contractId).OrderBy(l => l.Snapshot.DataDate).ThenBy(l => l.Id)
                        .Select(l => new { l.ActivityId, l.Percent, l.Snapshot.DataDate }).ToListAsync(cancellationToken);
                    var last = new Dictionary<int, decimal>();
                    int backfilled = 0;
                    foreach (var l in lines)
                    {
                        if (firstEvent.TryGetValue(l.ActivityId, out DateTime first) && l.DataDate.Date >= first) continue;
                        if (last.TryGetValue(l.ActivityId, out decimal before) ? before == l.Percent : l.Percent == 0m) continue;
                        last[l.ActivityId] = l.Percent;
                        PcProgressEvents.Add(_uow, contractId, l.ActivityId, l.DataDate, l.Percent, 0, PcProgressEvents.SourceBackfill);
                        backfilled++;
                    }
                    if (backfilled > 0) await _uow.SaveChangesAsync(cancellationToken);
                    // the night run before the working day starts sees the state of the previous day
                    await ApplyAsync(contractId, DateTime.Now.Hour < 6 ? DateTime.Today.AddDays(-1) : DateTime.Today, cancellationToken);
                    await _uow.SaveChangesAsync(cancellationToken);
                    done++;
                }
                catch (DbUpdateException) { ClearTracker(); }
                catch (InvalidOperationException) { ClearTracker(); }
            }
            return done;
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
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.ConfirmedByUserId, r.CreatorUserId }, cancellationToken);
            var dto = new PcDailyReportDto
            {
                Id = r.Id, ContractId = r.ContractId, Unit = r.Unit, ReportDate = r.ReportDate, ReportDateText = OfficeText.Date(r.ReportDate), State = r.State, ConfirmedAt = r.ConfirmedAt, ConfirmedAtText = OfficeText.Stamp(r.ConfirmedAt),
                ConfirmedByName = names.Name(r.ConfirmedByUserId), CreatorName = names.Name(r.CreatorUserId), Weather = r.Weather, Notes = r.Notes,
                Lines = r.Lines.OrderBy(l => l.Id).Select(l => new PcDailyLineDto
                {
                    ActivityId = l.ActivityId, ActivityCode = acts.FirstOrDefault(a => a.Id == l.ActivityId)?.Code, ActivityTitle = acts.FirstOrDefault(a => a.Id == l.ActivityId)?.Title, EventText = l.EventText,
                    Percent = l.Percent, IsLate = l.IsLate, DelayReasonCode = l.DelayReasonCode, DelayReason = l.DelayReason, PreventiveAction = l.PreventiveAction, CorrectiveAction = l.CorrectiveAction,
                }).ToList(),
                Manpower = (r.Manpower ?? new List<PcDailyManpower>()).OrderBy(x => x.Id).Select(x => new PcDailyManpowerDto { Kind = x.Kind, Title = x.Title, ActivityType = x.ActivityType, Count = x.Count, Present = x.Present, Absent = x.Absent, PersonDays = x.PersonDays, Note = x.Note }).ToList(),
                Materials = (r.Materials ?? new List<PcDailyMaterial>()).OrderBy(x => x.Id).Select(x => new PcDailyMaterialDto { ItemId = x.ItemId, Description = x.Description, Unit = x.Unit, Quantity = x.Quantity, Note = x.Note }).ToList(),
                Machinery = (r.Machinery ?? new List<PcDailyMachinery>()).OrderBy(x => x.Id).Select(x => new PcDailyMachineryDto { Name = x.Name, Ownership = x.Ownership, Present = x.Present, Active = x.Active, Hours = x.Hours, Note = x.Note }).ToList(),
                Obstacles = (r.Obstacles ?? new List<PcDailyObstacle>()).OrderBy(x => x.Id).Select(x => new PcDailyObstacleDto { Category = x.Category, Description = x.Description, ActivityId = x.ActivityId, ImpactHours = x.ImpactHours, Resolved = x.Resolved }).ToList(),
                HseEvents = (r.HseEvents ?? new List<PcDailyHse>()).OrderBy(x => x.Id).Select(x => new PcDailyHseDto { Kind = x.Kind, Description = x.Description, PersonsInvolved = x.PersonsInvolved, ActionTaken = x.ActionTaken }).ToList(),
            };
            dto.CanConfirm = r.State == 0 && PcDailyRules.MayConfirm(r.CreatorUserId, r.EditorUserIds, Me);
            return dto;
        }

        private IQueryable<PcDailyReport> DailyWithChildren() =>
            _uow.Set<PcDailyReport>().Include(r => r.Lines).Include(r => r.Manpower).Include(r => r.Materials).Include(r => r.Machinery).Include(r => r.Obstacles).Include(r => r.HseEvents).AsSplitQuery();

        public async Task<ResponseDto<PcDailyReportDto>> GetDailyByIdAsync(int id, CancellationToken cancellationToken)
        {
            PcDailyReport r = await DailyWithChildren().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (r == null) return PcText.NotFound<PcDailyReportDto>("گزارش روزانه پیدا نشد.");
            return ResponseDtoHelper.Ok(await DailyDtoAsync(r, cancellationToken));
        }

        public async Task<ResponseDto<PagedList<PcDailyListDto>>> GetDailyListAsync(PcDailyListQuery query, CancellationToken cancellationToken)
        {
            query ??= new PcDailyListQuery();
            IQueryable<PcDailyReport> q = _uow.Set<PcDailyReport>().AsNoTracking().Where(r => !r.Deleted && r.ContractId == query.ContractId);
            if (query.State.HasValue) q = q.Where(r => r.State == query.State.Value);
            if (query.From.HasValue) q = q.Where(r => r.ReportDate >= query.From.Value.Date);
            if (query.To.HasValue) q = q.Where(r => r.ReportDate <= query.To.Value.Date);
            PagedList<PcDailyReport> page = await PagedList<PcDailyReport>.CreateAsync(q.OrderByDescending(r => r.ReportDate).ThenBy(r => r.Unit), query.PageNumber, query.PageSize, cancellationToken);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, page.Select(r => r.ConfirmedByUserId), cancellationToken);
            List<int> pageIds = page.Select(r => r.Id).ToList();
            Dictionary<int, int> lineCounts = (await _uow.Set<PcDailyReportLine>().AsNoTracking().Where(l => pageIds.Contains(l.ReportId)).GroupBy(l => l.ReportId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(cancellationToken)).ToDictionary(x => x.Id, x => x.N);
            List<PcDailyListDto> rows = page.Select(r => new PcDailyListDto { Id = r.Id, ContractId = r.ContractId, Unit = r.Unit, ReportDate = r.ReportDate, ReportDateText = OfficeText.Date(r.ReportDate), State = r.State, LineCount = lineCounts.GetValueOrDefault(r.Id), ConfirmedByName = names.Name(r.ConfirmedByUserId) }).ToList();
            return ResponseDtoHelper.Ok((PagedList<PcDailyListDto>)new PcPage<PcDailyListDto>(rows, page.TotalCount, page.CurrentPage, page.PageSize));
        }

        public async Task<ResponseDto<PcAppliedProgress>> ApplyApprovedProgressAsync(PcApprovedProgress input, CancellationToken cancellationToken)
        {
            if (input == null) return Bad<PcAppliedProgress>(PcText.InvalidData);
            if (input.Percent < 0m || input.Percent > 100m) return Bad<PcAppliedProgress>("درصد پیشرفت باید بین ۰ و ۱۰۰ باشد.");
            PcActivity a = await _uow.Set<PcActivity>().FirstOrDefaultAsync(x => x.Id == input.ActivityId && !x.Deleted, cancellationToken);
            if (a == null) return PcText.NotFound<PcAppliedProgress>("فعالیت پیدا نشد.");
            // the method of the activity is never changed here: only an activity whose progress is reported by a person takes a report (the plan lock stays where it is)
            if (a.ProgressMethod != PcProgressMethod.Manual && a.ProgressMethod != PcProgressMethod.Contractor && a.ProgressMethod != PcProgressMethod.Quantity)
                return Bad<PcAppliedProgress>("پیشرفت این فعالیت از رویدادهای سیستم یا از وظایف محاسبه می‌شود و با گزارش دستی ثبت نمی‌شود.");
            if (await _uow.Set<PcManualProgress>().AnyAsync(m => m.ActivityId == a.Id && m.State == 0, cancellationToken)) return Bad<PcAppliedProgress>("یک پیشنهاد پیشرفت در انتظار تایید برنامه‌ریزی برای این فعالیت هست.");
            DateTime day = DateTime.Today;
            string dateProblem = await ProgressDateProblemAsync(a.ContractId, a.Id, day, cancellationToken);
            if (dateProblem != null) return Bad<PcAppliedProgress>(dateProblem);
            if (input.ActualStart.HasValue && input.ActualStart.Value.Date > day) return Bad<PcAppliedProgress>("تاریخ شروع واقعی در آینده است.");
            if (input.ActualFinish.HasValue && (input.ActualFinish.Value.Date > day || (input.ActualStart.HasValue && input.ActualFinish.Value.Date < input.ActualStart.Value.Date))) return Bad<PcAppliedProgress>("تاریخ پایان واقعی نامعتبر است.");
            if (input.Cost != 0m && !Enum.IsDefined(typeof(CostKind), input.CostKind)) return Bad<PcAppliedProgress>("نوع هزینه را مشخص کنید.");
            DateTimeOffset now = DateTimeOffset.Now;
            // actual dates reported by the resource are taken before the engine fills them from the events
            if (input.ActualStart.HasValue && !a.ActualStart.HasValue && input.Percent > 0m) a.ActualStart = input.ActualStart.Value.Date;
            if (input.ActualFinish.HasValue && input.Percent >= 100m && !a.ActualFinish.HasValue) a.ActualFinish = input.ActualFinish.Value.Date;
            var row = new PcManualProgress
            {
                ActivityId = a.Id, Percent = PcText.Round2(input.Percent), Comment = Clean(input.Comment), ProgressDate = day, ProposedByUserId = input.ProposerUserId ?? input.SubmittedByUserId, ProposedAt = now,
                State = 1, DecidedByUserId = input.ApprovedByUserId, DecidedAt = now, DecisionNote = "تایید زنجیره کارتابل تایید پیشرفت",
            };
            _uow.Set<PcManualProgress>().Add(row);
            PcProgressEvents.Add(_uow, a.ContractId, a.Id, day, row.Percent, input.ApprovedByUserId, PcProgressEvents.SourceManual);
            CostEntry cost = null;
            if (input.Cost != 0m)
            {
                await PcR1Upgrade.EnsureAsync(_uow, cancellationToken);
                cost = new CostEntry
                {
                    EntryDate = day, Kind = (CostKind)input.CostKind, ContractId = a.ContractId, WbsNodeId = a.WbsNodeId, Amount = PcText.Round2(input.Cost), Quantity = 0m, SourceType = ManualCostSource, SourceId = input.SubmissionId, SourceNumber = Clean(input.SubmissionNumber) ?? "PC",
                    Description = Clean(input.CostNote) ?? "هزینه گزارش پیشرفت", CreatedAt = now, CreatedByUserId = input.CostSetByUserId ?? input.SubmittedByUserId,
                };
                _uow.Set<CostEntry>().Add(cost);
            }
            return ResponseDtoHelper.Ok(new PcAppliedProgress { Manual = row, Cost = cost });
        }

        public async Task<ResponseDto<PcDailyReportDto>> GetDailyAsync(PcDailyQuery query, CancellationToken cancellationToken)
        {
            if (query == null) return Bad<PcDailyReportDto>(PcText.InvalidData);
            DateTime date = (query.Date ?? DateTime.Today).Date;
            string unit = Clean(query.Unit)?.ToUpperInvariant() ?? "ALL";
            PcDailyReport existing = await DailyWithChildren().AsNoTracking().FirstOrDefaultAsync(r => r.ContractId == query.ContractId && r.Unit == unit && r.ReportDate == date && !r.Deleted, cancellationToken);
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
            if (date > DateTime.Today) return Bad<PcDailyReportDto>("گزارش روزانه برای روز آینده ثبت نمی‌شود.");
            List<int> actIds = (dto.Lines ?? new List<PcDailyLineDto>()).Select(l => l.ActivityId).Distinct().ToList();
            if (actIds.Count != (dto.Lines ?? new List<PcDailyLineDto>()).Count) return Bad<PcDailyReportDto>("فعالیت تکراری در گزارش روزانه است.");
            if (await _uow.Set<PcActivity>().CountAsync(a => actIds.Contains(a.Id) && a.ContractId == dto.ContractId && !a.Deleted, cancellationToken) != actIds.Count) return Bad<PcDailyReportDto>("فعالیتی از این قرارداد نیست.");
            string problem = await DailyChildrenProblemAsync(dto, cancellationToken);
            if (problem != null) return Bad<PcDailyReportDto>(problem);
            DateTimeOffset now = DateTimeOffset.Now;
            PcDailyReport r = await DailyWithChildren().FirstOrDefaultAsync(x => x.ContractId == dto.ContractId && x.Unit == unit && x.ReportDate == date && !x.Deleted, cancellationToken);
            if (r == null)
            {
                r = new PcDailyReport { ContractId = dto.ContractId, Unit = unit, ReportDate = date, CreateDate = now, CreatorUserId = Me, Lines = new List<PcDailyReportLine>() };
                _uow.Set<PcDailyReport>().Add(r);
            }
            else
            {
                if (r.State == 1) return Bad<PcDailyReportDto>("گزارش تاییدشده تغییر نمی‌کند.");
                _uow.Set<PcDailyReportLine>().RemoveRange(r.Lines);
                _uow.Set<PcDailyManpower>().RemoveRange(r.Manpower);
                _uow.Set<PcDailyMaterial>().RemoveRange(r.Materials);
                _uow.Set<PcDailyMachinery>().RemoveRange(r.Machinery);
                _uow.Set<PcDailyObstacle>().RemoveRange(r.Obstacles);
                _uow.Set<PcDailyHse>().RemoveRange(r.HseEvents);
                r.Lines = new List<PcDailyReportLine>();
            }
            r.Manpower = new List<PcDailyManpower>(); r.Materials = new List<PcDailyMaterial>(); r.Machinery = new List<PcDailyMachinery>(); r.Obstacles = new List<PcDailyObstacle>(); r.HseEvents = new List<PcDailyHse>();
            foreach (PcDailyLineDto l in dto.Lines ?? new List<PcDailyLineDto>())
                r.Lines.Add(new PcDailyReportLine
                {
                    ActivityId = l.ActivityId, EventText = Clean(l.EventText), Percent = Math.Min(100m, Math.Max(0m, l.Percent)), IsLate = l.IsLate, DelayReasonCode = Clean(l.DelayReasonCode), DelayReason = Clean(l.DelayReason),
                    PreventiveAction = Clean(l.PreventiveAction), CorrectiveAction = Clean(l.CorrectiveAction),
                });
            foreach (PcDailyManpowerDto x in dto.Manpower ?? new List<PcDailyManpowerDto>())
                r.Manpower.Add(new PcDailyManpower { Kind = x.Kind, Title = OfficeText.Cap(Clean(x.Title), 200), ActivityType = OfficeText.Cap(Clean(x.ActivityType), 200), Count = x.Count, Present = x.Present, Absent = x.Absent, PersonDays = x.PersonDays, Note = OfficeText.Cap(Clean(x.Note), 500) });
            foreach (PcDailyMaterialDto x in dto.Materials ?? new List<PcDailyMaterialDto>())
                r.Materials.Add(new PcDailyMaterial { ItemId = x.ItemId, Description = OfficeText.Cap(Clean(x.Description), 300), Unit = OfficeText.Cap(Clean(x.Unit), 30), Quantity = x.Quantity, Note = OfficeText.Cap(Clean(x.Note), 500) });
            foreach (PcDailyMachineryDto x in dto.Machinery ?? new List<PcDailyMachineryDto>())
                r.Machinery.Add(new PcDailyMachinery { Name = OfficeText.Cap(Clean(x.Name), 200), Ownership = x.Ownership, Present = x.Present, Active = x.Active, Hours = x.Hours, Note = OfficeText.Cap(Clean(x.Note), 500) });
            foreach (PcDailyObstacleDto x in dto.Obstacles ?? new List<PcDailyObstacleDto>())
                r.Obstacles.Add(new PcDailyObstacle { Category = OfficeText.Cap(Clean(x.Category), 100), Description = OfficeText.Cap(Clean(x.Description), 1000), ActivityId = x.ActivityId, ImpactHours = x.ImpactHours, Resolved = x.Resolved });
            foreach (PcDailyHseDto x in dto.HseEvents ?? new List<PcDailyHseDto>())
                r.HseEvents.Add(new PcDailyHse { Kind = x.Kind, Description = OfficeText.Cap(Clean(x.Description), 1000), PersonsInvolved = x.PersonsInvolved, ActionTaken = OfficeText.Cap(Clean(x.ActionTaken), 1000) });
            r.Weather = OfficeText.Cap(Clean(dto.Weather), 100); r.Notes = OfficeText.Cap(Clean(dto.Notes), 4000);
            r.EditorUserIds = PcDailyRules.AddEditor(r.EditorUserIds, Me);
            r.ModifyDate = now; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await DailyDtoAsync(r, cancellationToken));
        }

        private async Task<string> DailyChildrenProblemAsync(PcDailyReportDto dto, CancellationToken cancellationToken)
        {
            foreach (PcDailyManpowerDto x in dto.Manpower ?? new List<PcDailyManpowerDto>())
            {
                if (!Enum.IsDefined(typeof(PcManpowerKind), x.Kind) || Clean(x.Title) == null) return "ردیف نیروی انسانی: عنوان (سمت یا شرکت) را بنویسید.";
                if (x.Count < 0 || x.Present < 0 || x.Absent < 0 || x.PersonDays < 0m) return "ردیف نیروی انسانی: عدد منفی پذیرفته نمی‌شود.";
                if (x.Kind == PcManpowerKind.Internal && x.Count > 0 && x.Present + x.Absent > x.Count) return "ردیف نیروی انسانی: جمع حاضر و غایب از تعداد بیشتر است.";
            }
            foreach (PcDailyMaterialDto x in dto.Materials ?? new List<PcDailyMaterialDto>())
                if (Clean(x.Description) == null || x.Quantity < 0m) return "ردیف مصالح: شرح را بنویسید و مقدار منفی نباشد.";
            foreach (PcDailyMachineryDto x in dto.Machinery ?? new List<PcDailyMachineryDto>())
            {
                if (Clean(x.Name) == null || !Enum.IsDefined(typeof(PcMachineOwnership), x.Ownership)) return "ردیف ماشین‌آلات: نام و نوع مالکیت (ملکی یا استیجاری) را مشخص کنید.";
                if (x.Present < 0 || x.Active < 0 || x.Hours < 0m || x.Active > x.Present) return "ردیف ماشین‌آلات: تعداد فعال از تعداد حاضر بیشتر نمی‌شود.";
            }
            List<int> obstacleActs = (dto.Obstacles ?? new List<PcDailyObstacleDto>()).Where(o => o.ActivityId.HasValue).Select(o => o.ActivityId.Value).Distinct().ToList();
            if (obstacleActs.Count > 0 && await _uow.Set<PcActivity>().CountAsync(a => obstacleActs.Contains(a.Id) && a.ContractId == dto.ContractId && !a.Deleted, cancellationToken) != obstacleActs.Count) return "فعالیت مانع از این پروژه نیست.";
            foreach (PcDailyObstacleDto x in dto.Obstacles ?? new List<PcDailyObstacleDto>())
                if (Clean(x.Description) == null || x.ImpactHours < 0m) return "ردیف موانع: شرح مانع را بنویسید.";
            foreach (PcDailyHseDto x in dto.HseEvents ?? new List<PcDailyHseDto>())
                if (!Enum.IsDefined(typeof(PcHseKind), x.Kind) || Clean(x.Description) == null || x.PersonsInvolved < 0) return "ردیف HSE: نوع و شرح رویداد را بنویسید (بدون نام افراد).";
            return null;
        }

        public async Task<ResponseDto<PcDailyReportDto>> ConfirmDailyAsync(int id, CancellationToken cancellationToken)
        {
            PcDailyReport r = await DailyWithChildren().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, cancellationToken);
            if (r == null) return PcText.NotFound<PcDailyReportDto>("گزارش روزانه پیدا نشد.");
            if (r.State == 1) return Bad<PcDailyReportDto>("گزارش قبلاً تایید شده است.");
            if (r.Unit != "ALL" ? !_user.IsInRole(r.Unit + ",PMO,PLANNING") : !_user.IsInRole(ProjectControlRoles.UnitManagers + ",PMO")) return Bad<PcDailyReportDto>(PcText.NotAllowed + " (گزارش را مدیر همان واحد تایید می‌کند)");
            if (!PcDailyRules.MayConfirm(r.CreatorUserId, r.EditorUserIds, Me)) return Bad<PcDailyReportDto>(PcText.SameUser + " (گزارش را کسی غیر از نویسنده یا ویرایشگر آن تایید می‌کند)");
            PcDailyReportLine bad = r.Lines.FirstOrDefault(l => l.IsLate && (Clean(l.DelayReasonCode) == null || Clean(l.DelayReason) == null || Clean(l.CorrectiveAction) == null));
            if (bad != null) return Bad<PcDailyReportDto>("برای فعالیت دارای تاخیر، کد و شرح دلیل تاخیر و اقدام اصلاحی لازم است.");
            r.State = 1; r.ConfirmedByUserId = Me; r.ConfirmedAt = DateTimeOffset.Now; r.ModifyDate = r.ConfirmedAt.Value; r.ModifierUserId = Me;
            await _uow.SaveChangesAsync(cancellationToken);
            return ResponseDtoHelper.Ok(await DailyDtoAsync(r, cancellationToken));
        }
    }
}
