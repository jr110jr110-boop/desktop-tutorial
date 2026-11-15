using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// What was really spent on a project, read from the other modules, aggregated: never one line per person. Only the ties that exist in the data model are used:
    ///  - production time bookings reach the project through the operation and its production order (MfTimeBooking, MfOperation, MfProductionOrder.ContractId); a booking is tied to a work centre by its operation;
    ///  - attendance sheets reach the project through the work orders they list hours for (FnAttendanceWorkOrder, WorkOrder.ContractId);
    ///  - purchase orders carry the contract and the supplier (PurchaseOrder.ContractId, ProviderId);
    ///  - warehouse issue documents carry the contract (InventoryDocument.ContractId) and the stock cost of their movements.
    /// A booking is not tied to a labour role, an activity of the schedule or a resource of the pool; an issue line is not tied to a material resource.
    /// </summary>
    internal static class PcResourceActuals
    {
        internal sealed class Result
        {
            public Dictionary<int, decimal> MachineHoursByCentre { get; } = new Dictionary<int, decimal>();
            public decimal BookedHours { get; set; }
            public int BookedEmployees { get; set; }
            public decimal AttendanceHours { get; set; }
            public int AttendanceEmployees { get; set; }
            public Dictionary<int, decimal> OrderedByProvider { get; } = new Dictionary<int, decimal>();
            public decimal IssuedCost { get; set; }
            public int IssueDocuments { get; set; }
        }

        /// <summary>The Persian year and month of a date as yyyymm (the attendance sheets are kept by Persian month).</summary>
        public static int PersianMonthKey(DateTime d)
        {
            var pc = new PersianCalendar();
            return pc.GetYear(d) * 100 + pc.GetMonth(d);
        }

        public static async Task<Result> LoadAsync(IUnitOfWork uow, int contractId, DateTime asOf, CancellationToken ct)
        {
            var result = new Result();
            DateTime end = asOf.Date.AddDays(1);

            // production time bookings: setup, run and rework count (waiting and cleaning are not work on the machine or by the person)
            var bookings = from b in uow.Set<MfTimeBooking>().AsNoTracking()
                           join o in uow.Set<MfOperation>().AsNoTracking() on b.OperationId equals o.Id
                           join po in uow.Set<MfProductionOrder>().AsNoTracking() on o.OrderId equals po.Id
                           where !b.Deleted && !o.Deleted && !po.Deleted && po.ContractId == contractId && b.StartAt < end
                                 && (b.Kind == MfBookingKind.Setup || b.Kind == MfBookingKind.Run || b.Kind == MfBookingKind.Rework)
                           select new { o.WorkCenterId, b.Hours, b.EmployeeId };
            foreach (var g in await bookings.Where(x => x.WorkCenterId != null).GroupBy(x => x.WorkCenterId).Select(g => new { Centre = g.Key, Hours = g.Sum(x => x.Hours) }).ToListAsync(ct))
                result.MachineHoursByCentre[g.Centre.Value] = g.Hours;
            result.BookedHours = await bookings.SumAsync(x => (decimal?)x.Hours, ct) ?? 0m;
            result.BookedEmployees = await bookings.Select(x => x.EmployeeId).Distinct().CountAsync(ct);

            // attendance: the hours that the monthly sheets list for the work orders of the contract, up to the Persian month of the data date
            int monthKey = PersianMonthKey(asOf);
            var attendance = from a in uow.Set<FnAttendanceWorkOrder>().AsNoTracking()
                             join w in uow.Set<WorkOrder>().AsNoTracking() on a.WorkOrderId equals w.Id
                             where w.ContractId == contractId && a.Sheet.Year * 100 + a.Sheet.Month <= monthKey
                             select new { a.Hours, a.Sheet.EmployeeId };
            result.AttendanceHours = await attendance.SumAsync(x => (decimal?)x.Hours, ct) ?? 0m;
            result.AttendanceEmployees = await attendance.Select(x => x.EmployeeId).Distinct().CountAsync(ct);

            // subcontract and services: the orders of the contract that were sent to the supplier (not draft, not cancelled), by supplier
            foreach (var g in await uow.Set<PurchaseOrder>().AsNoTracking().Where(p => !p.Deleted && p.ContractId == contractId && p.Kind != PurchaseKind.Goods && p.OrderDate < end
                        && (p.Status == PurchaseOrderStatus.Released || p.Status == PurchaseOrderStatus.PartlyReceived || p.Status == PurchaseOrderStatus.Received || p.Status == PurchaseOrderStatus.Closed))
                    .GroupBy(p => p.ProviderId).Select(g => new { Provider = g.Key, Amount = g.Sum(p => p.TotalAmount) }).ToListAsync(ct))
                result.OrderedByProvider[g.Provider] = g.Amount;

            // material: the stock cost of the confirmed issues of the contract (signed: the reversal of a cancelled issue is positive)
            var issues = from t in uow.Set<StockTransaction>().AsNoTracking()
                         join d in uow.Set<InventoryDocument>().AsNoTracking() on t.DocumentId equals d.Id
                         where !d.Deleted && d.ContractId == contractId && d.Type == InventoryDocumentType.Issue && t.TransactionDate < end
                         select new { t.CostAmount, t.DocumentId };
            decimal signed = await issues.SumAsync(x => (decimal?)x.CostAmount, ct) ?? 0m;
            result.IssuedCost = -signed;
            result.IssueDocuments = await issues.Select(x => x.DocumentId).Distinct().CountAsync(ct);
            return result;
        }
    }

    /// <summary>The planned value of a baseline row spread over the working days of its activity (pure, tested apart).</summary>
    internal static class PcPvRules
    {
        /// <summary>
        /// The working days of the calendar from start to finish (the next working day when there is none) with the quantity and the amount of each day by the distribution. The days add up to the quantity and to the amount exactly.
        /// </summary>
        public static List<(DateTime Day, decimal Quantity, decimal Amount)> Phase(PcWorkCalendar calendar, DateTime start, DateTime finish, decimal quantity, decimal amount, PcResourceDistribution distribution)
        {
            List<DateTime> days = PcLoadRules.WorkingDays(calendar, start, finish);
            if (days.Count == 0) days.Add(calendar.NextWorkingOnOrAfter(start.Date));
            decimal[] q = PcLoadRules.Spread(quantity, distribution, days.Count);
            decimal[] a = PcLoadRules.Spread(amount, distribution, days.Count);
            var result = new List<(DateTime, decimal, decimal)>();
            for (int i = 0; i < days.Count; i++) result.Add((days[i], q[i], a[i]));
            return result;
        }

        public static string PeriodKey(DateTime d)
        {
            var pc = new PersianCalendar();
            return pc.GetYear(d).ToString("0000", CultureInfo.InvariantCulture) + "/" + pc.GetMonth(d).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The first and last day (Gregorian dates) of the Persian month that holds the date.</summary>
        public static (DateTime From, DateTime To) PeriodRange(DateTime d)
        {
            var pc = new PersianCalendar();
            int y = pc.GetYear(d), m = pc.GetMonth(d);
            DateTime from = pc.ToDateTime(y, m, 1, 0, 0, 0, 0);
            DateTime to = pc.ToDateTime(y, m, pc.GetDaysInMonth(y, m), 0, 0, 0, 0);
            return (from, to);
        }

        public static string Digits(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c >= '0' && c <= '9' ? (char)('۰' + (c - '0')) : c);
            return sb.ToString();
        }
    }
}
