using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>
    /// R1c (H4, data-date discipline): the dated observations of the physical percent of the activities. Earned value at a data date is read from these rows
    /// (the latest event on or before the date), never from the module data of today, so a figure for a past date cannot be changed by what happened afterwards.
    /// </summary>
    internal static class PcProgressEvents
    {
        public const string SourceEngine = "engine";
        public const string SourceManual = "manual";
        public const string SourceBackfill = "backfill";

        /// <summary>The events of a contract in the order in which they count (date, then id).</summary>
        public static Task<List<PcProgressEvent>> ListAsync(IUnitOfWork uow, int contractId, CancellationToken cancellationToken) =>
            uow.Set<PcProgressEvent>().AsNoTracking().Where(e => e.ContractId == contractId).OrderBy(e => e.EventDate).ThenBy(e => e.Id).ToListAsync(cancellationToken);

        /// <summary>The percent of every activity as known at the end of the day: the latest event on or before it. An activity without an event is not in the result (zero percent).</summary>
        public static Dictionary<int, decimal> AsOf(IEnumerable<PcProgressEvent> events, DateTime asOf)
        {
            var result = new Dictionary<int, decimal>();
            foreach (PcProgressEvent e in events.Where(e => e.EventDate.Date <= asOf.Date).OrderBy(e => e.EventDate).ThenBy(e => e.Id)) result[e.ActivityId] = e.Percent;
            return result;
        }

        public static async Task<Dictionary<int, decimal>> AsOfAsync(IUnitOfWork uow, int contractId, DateTime asOf, CancellationToken cancellationToken) =>
            AsOf(await ListAsync(uow, contractId, cancellationToken), asOf);

        /// <summary>
        /// Adds (not saved) an event for every activity whose percent differs from its latest event. An activity with no event yet and zero percent gets none.
        /// Returns the number of events added.
        /// </summary>
        /// <param name="eventDate">The day the change was seen (the latest day an event may carry).</param>
        /// <param name="factDates">
        /// R1c (C9): the day of the fact behind the new percent (receipt, QC result, approval, release, shipment) per activity; an activity without a date gets <paramref name="eventDate"/>.
        /// A fact date is never in the future, never before the day after the last weekly snapshot (a reported week does not change) and never before the latest event of the same activity.
        /// </param>
        public static async Task<int> RecordAsync(IUnitOfWork uow, int contractId, IDictionary<int, decimal> percents, DateTime eventDate, int userId, string source, CancellationToken cancellationToken, IDictionary<int, DateTime> factDates = null)
        {
            List<PcProgressEvent> events = await ListAsync(uow, contractId, cancellationToken);
            Dictionary<int, decimal> latest = AsOf(events, DateTime.MaxValue.Date);
            Dictionary<int, DateTime> latestDate = events.GroupBy(e => e.ActivityId).ToDictionary(g => g.Key, g => g.Max(e => e.EventDate.Date));
            DateTime? lastSnapshot = factDates == null ? null : await uow.Set<PcProgressSnapshot>().AsNoTracking().Where(s => s.ContractId == contractId).MaxAsync(s => (DateTime?)s.DataDate, cancellationToken);
            DateTimeOffset now = DateTimeOffset.Now;
            int added = 0;
            foreach (KeyValuePair<int, decimal> kv in percents.OrderBy(k => k.Key))
            {
                decimal percent = PcText.Round4(kv.Value);
                bool known = latest.TryGetValue(kv.Key, out decimal last);
                if (known ? last == percent : percent == 0m) continue;
                DateTime date = eventDate.Date;
                if (factDates != null && factDates.TryGetValue(kv.Key, out DateTime fact))
                {
                    date = fact.Date < date ? fact.Date : date;
                    if (lastSnapshot.HasValue && date <= lastSnapshot.Value.Date) date = lastSnapshot.Value.Date.AddDays(1);
                    if (latestDate.TryGetValue(kv.Key, out DateTime prevDate) && date < prevDate) date = prevDate;
                    if (date > eventDate.Date) date = eventDate.Date;
                }
                uow.Set<PcProgressEvent>().Add(new PcProgressEvent { ContractId = contractId, ActivityId = kv.Key, EventDate = date, Percent = percent, RecordedAt = now, RecordedByUserId = userId, Source = source });
                added++;
            }
            return added;
        }

        /// <summary>Adds (not saved) one event for one activity, whatever the latest one says (an approved manual progress).</summary>
        public static void Add(IUnitOfWork uow, int contractId, int activityId, DateTime eventDate, decimal percent, int userId, string source) =>
            uow.Set<PcProgressEvent>().Add(new PcProgressEvent { ContractId = contractId, ActivityId = activityId, EventDate = eventDate.Date, Percent = PcText.Round4(percent), RecordedAt = DateTimeOffset.Now, RecordedByUserId = userId, Source = source });
    }
}
