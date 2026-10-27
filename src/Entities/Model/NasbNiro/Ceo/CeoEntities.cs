using System;

namespace Entities.Model.NasbNiro.Ceo
{
    // CEO executive dashboard (release D1). Tables start with "Ceo". Nothing here is a source of truth:
    // a snapshot row only remembers what a figure of the dashboard was on a day, so a trend can be drawn
    // for figures that have no transactional history (cash balance, stock value, open counts ...).

    /// <summary>
    /// One figure of the CEO dashboard on one day (nightly job at 00:30 or the "snapshot now" action).
    /// There is one row per (Date, Key); running the job again on the same day replaces the value.
    /// </summary>
    public class CeoKpiSnapshot
    {
        public int Id { get; set; }

        /// <summary>The server day the figure was taken on (date only).</summary>
        public DateTime Date { get; set; }

        /// <summary>For example "cash", "receivablesOpen", "stockValue" (the same keys the overview uses). 60 characters at most.</summary>
        public string Key { get; set; }

        /// <summary>The figure: rial for money, a number for counts.</summary>
        public decimal Value { get; set; }

        /// <summary>The moment the value was taken; a re-run on the same day refreshes it.</summary>
        public DateTimeOffset CreatedAt { get; set; }
    }
}
