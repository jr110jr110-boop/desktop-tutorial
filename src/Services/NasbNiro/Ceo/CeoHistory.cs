using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Services.NasbNiro.AfterSales;

namespace Services.NasbNiro.Ceo
{
    /// <summary>
    /// ★ The month-end history that the records themselves can give, so that the trends show something before the nightly snapshots have lived through a month.
    /// Four keys can be derived without a new table: <c>cash</c> (the accounting events of the cash boxes and bank accounts up to each month end),
    /// <c>receivablesOpen</c> (the as-of ageing of the receivables module on each month end), <c>openNcrs</c> (NCRs reported on or before the day and not closed or cancelled by then)
    /// and <c>serviceSlaBreaches</c> (the requests that were open at the end of the day and past their response or resolution due time at that moment).
    /// A month is derived only when no real snapshot covers it and only from the month of the first record of its source (before that there is no history, not a zero);
    /// a real snapshot always wins. The other eight snapshot keys cannot be derived (they are states of the moment: payables ageing, backlog, projects, late orders,
    /// stock value, items below the minimum, open purchase orders, pipeline) and start with the live point alone.
    /// The derivation follows the live definitions of the figures, so the last derived month and the live value agree when nothing was back-dated.
    /// </summary>
    public static class CeoHistory
    {
        /// <summary>The snapshot keys that can be derived from the records.</summary>
        public static readonly string[] Keys = { CeoKeys.Cash, CeoKeys.ReceivablesOpen, CeoKeys.OpenNcrs, CeoKeys.ServiceSlaBreaches };

        private static readonly SvRequestStatus[] OpenStatuses = { SvRequestStatus.Open, SvRequestStatus.Assigned, SvRequestStatus.InProgress, SvRequestStatus.WaitingCustomer };

        /// <summary>The Persian months of the trend window that end before the current month: the months a month-end value can exist for.</summary>
        public static List<DateTime> PastMonths(DateTime today) => CeoTrends.Window(today).Where(m => m < CeoTrends.MonthStart(today)).ToList();

        /// <summary>
        /// The derived month-end points by key. <paramref name="covered"/> says, for each key, the months a real snapshot already covers (they are not derived).
        /// A key whose derivation fails is left out (the failure is logged) and the others are kept.
        /// </summary>
        /// <param name="receivablesOpenOn">The open receivables on a day (the as-of ageing total), or null when they cannot be read.</param>
        public static async Task<Dictionary<string, List<CeoTrendPoint>>> DeriveAsync(IUnitOfWork uow, Func<DateTime, CancellationToken, Task<decimal?>> receivablesOpenOn, DateTime today,
            IReadOnlyDictionary<string, HashSet<DateTime>> covered, ILogger logger, CancellationToken ct)
        {
            var result = new Dictionary<string, List<CeoTrendPoint>>();
            foreach (string key in Keys)
            {
                List<DateTime> months = MissingMonths(today, covered != null && covered.TryGetValue(key, out HashSet<DateTime> have) ? have : null);
                if (months.Count == 0) continue;
                List<CeoTrendPoint> points = await DeriveKeyAsync(uow, key, receivablesOpenOn, months, logger, ct);
                if (points != null && points.Count > 0) result[key] = points;
            }
            return result;
        }

        /// <summary>The past months of the window that no real snapshot covers.</summary>
        public static List<DateTime> MissingMonths(DateTime today, ISet<DateTime> covered) => PastMonths(today).Where(m => covered == null || !covered.Contains(m)).ToList();

        /// <summary>
        /// The derived month-end points of one key for the given months (oldest first; a month before the first record of the source has no point, so the list may be empty).
        /// Returns null when the derivation failed (it is logged as a warning), so that a caller can tell "no history" from "could not be read" and not remember the failure.
        /// </summary>
        public static async Task<List<CeoTrendPoint>> DeriveKeyAsync(IUnitOfWork uow, string key, Func<DateTime, CancellationToken, Task<decimal?>> receivablesOpenOn, List<DateTime> months, ILogger logger, CancellationToken ct)
        {
            if (months == null || months.Count == 0) return new List<CeoTrendPoint>();
            try
            {
                switch (key)
                {
                    case CeoKeys.Cash: return await CashAsync(uow, months, ct);
                    case CeoKeys.ReceivablesOpen: return await ReceivablesAsync(uow, receivablesOpenOn, months, ct);
                    case CeoKeys.OpenNcrs: return await OpenNcrsAsync(uow, months, ct);
                    case CeoKeys.ServiceSlaBreaches: return await SlaBreachesAsync(uow, months, ct);
                    default: throw new ArgumentOutOfRangeException(nameof(key), key, "This key cannot be derived from the records.");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (ArgumentOutOfRangeException) { throw; }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "CEO overview: the history of '{Key}' could not be derived from the records; its trend starts with the live value.", key);
                return null;
            }
        }

        private static CeoTrendPoint Point(DateTime month, DateTime end, decimal value) => new CeoTrendPoint { Month = month, Date = end, Value = value };

        private static DateTime EndOf(DateTime month) => CeoTrends.Span(month).To;

        /// <summary>Cash and bank at a month end: the net of the accounting events of the cash boxes and bank accounts up to that day (the sum the dashboard shows, without the later events).</summary>
        private static async Task<List<CeoTrendPoint>> CashAsync(IUnitOfWork uow, List<DateTime> months, CancellationToken ct)
        {
            DateTime before = EndOf(months.Max()).AddDays(1);
            var daily = await uow.Set<AccountingEventLine>().AsNoTracking()
                .Where(l => l.Event.Status != AccountingEventStatus.NotRequired && (l.PartyType == "CashBox" || l.PartyType == "BankAccount") && l.Event.EventDate < before)
                .GroupBy(l => l.Event.EventDate.Date).Select(g => new { Day = g.Key, Net = g.Sum(l => l.Debit - l.Credit) }).ToListAsync(ct);
            var points = new List<CeoTrendPoint>();
            if (daily.Count == 0) return points;
            DateTime first = daily.Min(d => d.Day);
            foreach (DateTime month in months)
            {
                DateTime end = EndOf(month);
                if (end < first) continue;
                points.Add(Point(month, end, daily.Where(d => d.Day <= end).Sum(d => d.Net)));
            }
            return points;
        }

        /// <summary>Open receivables at a month end: the total of the as-of ageing of the receivables module for that day, from the month of the first posted invoice.</summary>
        private static async Task<List<CeoTrendPoint>> ReceivablesAsync(IUnitOfWork uow, Func<DateTime, CancellationToken, Task<decimal?>> receivablesOpenOn, List<DateTime> months, CancellationToken ct)
        {
            var points = new List<CeoTrendPoint>();
            if (receivablesOpenOn == null) return points;
            DateTime? first = await uow.Set<FnSalesInvoice>().AsNoTracking().Where(i => i.Status == FnDocStatus.Posted && i.Type != FnInvoiceType.CreditNote).MinAsync(i => (DateTime?)i.Date, ct);
            if (!first.HasValue) return points;
            foreach (DateTime month in months)
            {
                DateTime end = EndOf(month);
                if (end < first.Value.Date) continue;
                decimal? total = await receivablesOpenOn(end, ct);
                if (total.HasValue) points.Add(Point(month, end, total.Value));
            }
            return points;
        }

        /// <summary>
        /// Open NCRs at a month end: reported on or before the day, not cancelled, and not closed by the end of the day. A closed NCR without a closing time is taken as closed on its report day.
        /// </summary>
        private static async Task<List<CeoTrendPoint>> OpenNcrsAsync(IUnitOfWork uow, List<DateTime> months, CancellationToken ct)
        {
            var points = new List<CeoTrendPoint>();
            IQueryable<NonConformance> live = uow.Set<NonConformance>().AsNoTracking().Where(n => !n.Deleted && n.Status != NcrStatus.Cancelled);
            DateTime? first = await live.MinAsync(n => (DateTime?)n.ReportDate, ct);
            if (!first.HasValue) return points;
            DateTime before = EndOf(months.Max()).AddDays(1);
            DateTimeOffset windowStart = new DateTimeOffset(months.Min());
            var ncrs = await live.Where(n => n.ReportDate < before && (n.Status != NcrStatus.Closed || n.ClosedAt == null || n.ClosedAt >= windowStart))
                .Select(n => new { n.ReportDate, n.Status, n.ClosedAt }).ToListAsync(ct);
            foreach (DateTime month in months)
            {
                DateTime end = EndOf(month);
                if (end < first.Value.Date) continue;
                int open = ncrs.Count(n =>
                {
                    if (n.ReportDate.Date > end) return false;
                    if (n.Status != NcrStatus.Closed) return true;
                    DateTime closed = n.ClosedAt.HasValue ? n.ClosedAt.Value.LocalDateTime.Date : n.ReportDate.Date;
                    return closed > end;
                });
                points.Add(Point(month, end, open));
            }
            return points;
        }

        /// <summary>
        /// SLA breaches at a month end, judged at the end of that day as the service dashboard judges them now: a request that was open (received, not yet resolved or closed)
        /// whose response due time passed without a first response, or whose resolution due time passed.
        /// </summary>
        private static async Task<List<CeoTrendPoint>> SlaBreachesAsync(IUnitOfWork uow, List<DateTime> months, CancellationToken ct)
        {
            var points = new List<CeoTrendPoint>();
            IQueryable<SvServiceRequest> live = uow.Set<SvServiceRequest>().AsNoTracking().Where(r => !r.Deleted);
            DateTime? first = await live.MinAsync(r => (DateTime?)r.ReceivedAt, ct);
            if (!first.HasValue) return points;
            DateTime before = EndOf(months.Max()).AddDays(1);
            DateTime windowStart = months.Min();
            DateTimeOffset windowStartOffset = new DateTimeOffset(windowStart);
            var requests = await live.Where(r => r.ReceivedAt < before && (OpenStatuses.Contains(r.Status) || r.ResolvedAt == null || r.ResolvedAt >= windowStart || (r.ClosedAt != null && r.ClosedAt >= windowStartOffset)))
                .Select(r => new { r.Status, r.ReceivedAt, r.ResponseDue, r.ResolutionDue, r.FirstResponseAt, r.ResolvedAt, r.ClosedAt }).ToListAsync(ct);
            foreach (DateTime month in months)
            {
                DateTime end = EndOf(month);
                if (end < first.Value.Date) continue;
                DateTime cut = end.AddDays(1);
                int breaches = requests.Count(r =>
                {
                    if (r.ReceivedAt >= cut) return false;
                    if (!OpenStatuses.Contains(r.Status))
                    {
                        DateTime left = r.ResolvedAt ?? (r.ClosedAt.HasValue ? r.ClosedAt.Value.LocalDateTime : r.ReceivedAt);
                        if (left < cut) return false;
                    }
                    DateTime? responded = r.FirstResponseAt.HasValue && r.FirstResponseAt.Value < cut ? r.FirstResponseAt : null;
                    DateTime? resolved = r.ResolvedAt.HasValue && r.ResolvedAt.Value < cut ? r.ResolvedAt : null;
                    return SvSla.Breached(r.ResponseDue, responded, cut) || SvSla.Breached(r.ResolutionDue, resolved, cut);
                });
                points.Add(Point(month, end, breaches));
            }
            return points;
        }
    }
}
