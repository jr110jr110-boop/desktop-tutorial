using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Production
{
    internal static class MfMapping
    {
        public static string JobCard(string orderNumber, MfOperation op) => MfNames.Operation(orderNumber, op.Sequence) + (op.IsRework ? "-R" : string.Empty);

        public static async Task<List<OperationDto>> OperationDtosAsync(IUnitOfWork _uow, List<MfOperation> ops, CancellationToken ct)
        {
            if (ops.Count == 0) return new List<OperationDto>();
            List<int> orderIds = ops.Select(o => o.OrderId).Distinct().ToList();
            var orderRows = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Number, o.Quantity }).ToListAsync(ct);
            Dictionary<int, string> orders = orderRows.ToDictionary(o => o.Id, o => o.Number);
            // R1p D7: the expected quantity of an operation comes from the chain of all operations of its order (the operation before it), so the whole chain is read
            List<MfOperation> chainRows = await _uow.Set<MfOperation>().AsNoTracking().Where(o => !o.Deleted && orderIds.Contains(o.OrderId)).ToListAsync(ct);
            List<int> wpsIds = ops.Where(o => o.WpsId.HasValue).Select(o => o.WpsId.Value).Distinct().ToList();
            Dictionary<int, string> wpsNumbers = wpsIds.Count == 0 ? new Dictionary<int, string>()
                : (await _uow.Set<Entities.Model.NasbNiro.Quality.Wps>().AsNoTracking().Where(w => wpsIds.Contains(w.Id)).Select(w => new { w.Id, w.Number, w.Revision }).ToListAsync(ct)).ToDictionary(w => w.Id, w => w.Number + "/" + w.Revision);
            List<int> centerIds = ops.Where(o => o.WorkCenterId.HasValue).Select(o => o.WorkCenterId.Value).Distinct().ToList();
            var centers = centerIds.Count == 0 ? new Dictionary<int, MfWorkCenter>()
                : await _uow.Set<MfWorkCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            List<int> qcIds = ops.Where(o => o.QcRecordId.HasValue).Select(o => o.QcRecordId.Value).ToList();
            Dictionary<int, string> qcs = qcIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<QcRecord>().AsNoTracking().Where(q => qcIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id, q => q.Number, ct);
            List<int> ncrIds = ops.Where(o => o.NcrId.HasValue).Select(o => o.NcrId.Value).ToList();
            Dictionary<int, string> ncrs = ncrIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<NonConformance>().AsNoTracking().Where(n => ncrIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Number, ct);
            List<int> prIds = ops.Where(o => o.PurchaseRequestId.HasValue).Select(o => o.PurchaseRequestId.Value).ToList();
            Dictionary<int, string> prs = prIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<PurchaseRequest>().AsNoTracking().Where(p => prIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);
            List<int> docIds = ops.Where(o => o.SubcontractIssueId.HasValue).Select(o => o.SubcontractIssueId.Value).ToList();
            Dictionary<int, string> docs = docIds.Count == 0 ? new Dictionary<int, string>() : await _uow.Set<InventoryDocument>().AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
            List<int> opIds = ops.Select(o => o.Id).ToList();
            var bookings = await _uow.Set<MfTimeBooking>().AsNoTracking().Where(b => !b.Deleted && opIds.Contains(b.OperationId))
                .GroupBy(b => b.OperationId).Select(g => new { Id = g.Key, Running = g.Count(b => b.EndAt == null), Hours = g.Sum(b => b.Hours) }).ToListAsync(ct);
            return ops.Select(o =>
            {
                orders.TryGetValue(o.OrderId, out string number);
                MfWorkCenter wc = o.WorkCenterId.HasValue && centers.TryGetValue(o.WorkCenterId.Value, out MfWorkCenter c) ? c : null;
                var b = bookings.FirstOrDefault(x => x.Id == o.Id);
                return new OperationDto
                {
                    Id = o.Id, OrderId = o.OrderId, OrderNumber = number, JobCard = JobCard(number, o), Sequence = o.Sequence, Title = o.Title, WorkCenterId = o.WorkCenterId,
                    WorkCenterCode = wc?.Code, WorkCenterTitle = wc?.Title, SetupHoursPlanned = o.SetupHoursPlanned, RunHoursPlanned = o.RunHoursPlanned,
                    PlannedStart = o.PlannedStart, PlannedStartText = MfText.Date(o.PlannedStart), PlannedFinish = o.PlannedFinish, PlannedFinishText = MfText.Date(o.PlannedFinish),
                    Status = o.Status, GoodQty = o.GoodQty, ScrapQty = o.ScrapQty, ReworkQty = o.ReworkQty, InspectionRequirement = o.InspectionRequirement, ItpTestType = o.ItpTestType,
                    ItpActivityId = o.ItpActivityId, QcAccepted = o.QcAccepted, QcRequestedAtText = MfText.Stamp(o.QcRequestedAt), QcRecordId = o.QcRecordId,
                    QcRecordNumber = o.QcRecordId.HasValue && qcs.TryGetValue(o.QcRecordId.Value, out string q) ? q : null, NcrId = o.NcrId,
                    NcrNumber = o.NcrId.HasValue && ncrs.TryGetValue(o.NcrId.Value, out string n) ? n : null, IsOutside = o.IsOutside, OutsideSupplierId = o.OutsideSupplierId,
                    PurchaseRequestId = o.PurchaseRequestId, PurchaseRequestNumber = o.PurchaseRequestId.HasValue && prs.TryGetValue(o.PurchaseRequestId.Value, out string p) ? p : null,
                    PurchaseRequestLineId = o.PurchaseRequestLineId, PurchaseOrderLineId = o.PurchaseOrderLineId, SubcontractIssueId = o.SubcontractIssueId,
                    SubcontractIssueNumber = o.SubcontractIssueId.HasValue && docs.TryGetValue(o.SubcontractIssueId.Value, out string d) ? d : null, IsRework = o.IsRework,
                    ReworkOfOperationId = o.ReworkOfOperationId, SkipReason = o.SkipReason, StartedAtText = MfText.Stamp(o.StartedAt), FinishedAtText = MfText.Stamp(o.FinishedAt), Note = o.Note,
                    RunningBookings = b?.Running ?? 0, HoursBooked = b?.Hours ?? 0,
                    ExpectedQty = MfQuantityRules.Expected(chainRows.Where(x => x.OrderId == o.OrderId), o, orderRows.First(x => x.Id == o.OrderId).Quantity),
                    IsWelding = o.IsWelding, WpsId = o.WpsId, WpsNumber = o.WpsId.HasValue && wpsNumbers.TryGetValue(o.WpsId.Value, out string wn) ? wn : null, WeldLogId = o.WeldLogId,
                    AcceptedByConcession = o.AcceptedByConcession, AcceptanceStale = o.QcAccepted && !MfTerminalRules.AcceptanceStillHolds(o),
                };
            }).OrderBy(d => d.OrderId).ThenBy(d => d.Sequence).ThenBy(d => d.IsRework ? 1 : 0).ToList();
        }

    }
}
