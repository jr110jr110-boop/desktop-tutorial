using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using static Services.NasbNiro.Finance.LedgerMath;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// R1t D10: the period and year close. A period is locked by an accountant and closed by a second person (the CEO by default, never the one who locked it);
    /// a red checklist item or an unclean reconciliation is passed only with the request of one finance manager and the approval of another;
    /// and nothing is closed while the period still holds work that is not finished (events not posted, draft vouchers, treasury documents on the way, open payroll runs).
    /// </summary>
    public partial class GlService
    {
        private const string OverrideItemSeparator = " | ";

        /// <summary>The role that takes the second step of a period close (setting PeriodClose.CloseBy; CEO unless the company chose its finance manager).</summary>
        private async Task<string> CloseByRoleAsync(CancellationToken ct)
        {
            string value = (await SettingAsync(FinanceSeed.SettingPeriodCloseBy, "CEO", ct) ?? "CEO").Trim().ToUpperInvariant();
            return value == "FINANCEMANAGER" ? FinanceRoles.Managers : FinanceRoles.Ceo;
        }

        /// <summary>The user who locked the period last (0 when unknown, for a period locked before the log existed).</summary>
        /// <summary>
        /// H1 (review R1t): the periods of a year that were NOT closed through the two-step period close - still open or only locked, or "closed" without a close row of somebody other than the last locker in the period log.
        /// A year is closed only when this list is empty. Returns the period numbers.
        /// </summary>
        internal async Task<List<int>> PeriodsNotClosedProperlyAsync(FnFiscalYear year, CancellationToken ct)
        {
            var bad = new List<int>();
            List<int> ids = year.Periods.Select(p => p.Id).ToList();
            var logs = await _uow.Set<FnPeriodLog>().AsNoTracking().Where(l => ids.Contains(l.PeriodId) && (l.Action == "lock" || l.Action == "close")).OrderBy(l => l.Id)
                .Select(l => new { l.PeriodId, l.Action, l.UserId }).ToListAsync(ct);
            foreach (FnPeriod p in year.Periods.OrderBy(x => x.Number))
            {
                if (p.Status != FnPeriodStatus.Closed) { bad.Add(p.Number); continue; }
                var mine = logs.Where(l => l.PeriodId == p.Id).ToList();
                var close = mine.LastOrDefault(l => l.Action == "close");
                var lockRow = close == null ? null : mine.LastOrDefault(l => l.Action == "lock" && mine.IndexOf(l) < mine.IndexOf(close));
                if (close == null || lockRow == null || close.UserId == 0 || lockRow.UserId == 0 || close.UserId == lockRow.UserId) bad.Add(p.Number);
            }
            return bad;
        }

        private async Task<int> LastLockerAsync(int periodId, CancellationToken ct) =>
            await _uow.Set<FnPeriodLog>().AsNoTracking().Where(l => l.PeriodId == periodId && l.Action == "lock").OrderByDescending(l => l.Id).Select(l => l.UserId).FirstOrDefaultAsync(ct);

        /// <summary>
        /// What is still unfinished between two dates, as Persian phrases for the refusal message; empty when the range may be closed.
        /// Draft vouchers are counted inside the fiscal year, events and treasury documents by their dates.
        /// </summary>
        internal async Task<List<string>> CloseBlockersAsync(DateTime start, DateTime end, int entityId, int fiscalYearId, CancellationToken ct)
        {
            var blockers = new List<string>();
            DateTime from = start.Date, to = end.Date;

            int open = await _uow.Set<FnVoucher>().CountAsync(v => v.FiscalYearId == fiscalYearId && v.Date >= from && v.Date <= to && v.Status != FnVoucherStatus.Final, ct);
            if (open > 0) blockers.Add($"{open} سند قطعی‌نشده (پیش‌نویس یا کنترل‌شده) در این بازه هست؛ ابتدا همه اسناد قطعی شوند");

            AccountingEventStatus[] unposted = { AccountingEventStatus.Pending, AccountingEventStatus.Exported, AccountingEventStatus.Failed, AccountingEventStatus.Sent };
            int events = await _uow.Set<AccountingEvent>().CountAsync(e => unposted.Contains(e.Status) && e.EventDate >= from && e.EventDate <= to
                && (e.LegalEntityId == entityId || (entityId == FinanceSeed.MainEntityId && e.LegalEntityId == 0)), ct);
            if (events > 0) blockers.Add($"{events} رویداد مالی هنوز به سند دفتر کل تبدیل نشده است");

            // treasury documents that are not posted or cancelled: the money has not moved yet but the document is dated in the range
            int payments = await _uow.Set<FnPayment>().CountAsync(p => p.Date >= from && p.Date <= to && (p.Status == FnDocStatus.Draft || p.Status == FnDocStatus.Approved), ct);
            int receipts = await _uow.Set<FnReceipt>().CountAsync(r => r.Date >= from && r.Date <= to && (r.Status == FnDocStatus.Draft || r.Status == FnDocStatus.Approved), ct);
            if (payments + receipts > 0) blockers.Add($"{payments + receipts} سند خزانه (پرداخت یا دریافت) نه ثبت شده و نه ابطال شده است");

            int fromIndex = PersianCal.Index(from), toIndex = PersianCal.Index(to);
            var runs = await _uow.Set<FnPayrollRun>().AsNoTracking().Where(r => r.Status != FnPayrollStatus.Posted && r.Status != FnPayrollStatus.Paid)
                .Select(r => new { r.Year, r.Month, r.Status }).ToListAsync(ct);
            foreach (var run in runs.Where(r => PersianCal.Index(r.Year, r.Month) >= fromIndex && PersianCal.Index(r.Year, r.Month) <= toIndex).OrderBy(r => r.Year).ThenBy(r => r.Month))
                blockers.Add($"فهرست حقوق {run.Year.ToString(CultureInfo.InvariantCulture)}/{run.Month.ToString("00", CultureInfo.InvariantCulture)} هنوز باز است ({PayrollStatusText(run.Status)})");
            return blockers;
        }

        private static string PayrollStatusText(FnPayrollStatus status)
        {
            switch (status)
            {
                case FnPayrollStatus.Draft: return "پیش‌نویس";
                case FnPayrollStatus.Calculated: return "محاسبه‌شده";
                case FnPayrollStatus.Approved: return "تاییدشده و ثبت‌نشده";
                default: return status.ToString();
            }
        }

        private static string BlockersMessage(string what, List<string> blockers) => $"بستن {what} ممکن نیست: " + string.Join("؛ ", blockers) + ".";

        // ------------------------------------------------------------------------------------------ overrides (two managers)

        private async Task<FnCloseOverride> PendingOverrideAsync(FnCloseOverrideKind kind, int? periodId, int? fiscalYearId, CancellationToken ct) =>
            await _uow.Set<FnCloseOverride>().Where(o => o.Kind == kind && o.PeriodId == periodId && o.FiscalYearId == fiscalYearId && o.Status == FnCloseOverrideStatus.Pending)
                .OrderByDescending(o => o.Id).FirstOrDefaultAsync(ct);

        private async Task<FnCloseOverride> ApprovedOverrideAsync(FnCloseOverrideKind kind, int? periodId, int? fiscalYearId, CancellationToken ct) =>
            await _uow.Set<FnCloseOverride>().AsNoTracking().Where(o => o.Kind == kind && o.PeriodId == periodId && o.FiscalYearId == fiscalYearId && o.Status == FnCloseOverrideStatus.Approved)
                .OrderByDescending(o => o.Id).FirstOrDefaultAsync(ct);

        /// <summary>True when every red key is among the keys the approved override covers.</summary>
        internal static bool OverrideCovers(string coveredItems, IEnumerable<string> redKeys)
        {
            HashSet<string> covered = (coveredItems ?? "").Split(new[] { OverrideItemSeparator.Trim() }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return redKeys.All(covered.Contains);
        }

        internal static string JoinOverrideItems(IEnumerable<string> keys)
        {
            string text = string.Join(OverrideItemSeparator, keys);
            return text.Length > 2000 ? text.Substring(0, 2000) : text;
        }
    }
}
