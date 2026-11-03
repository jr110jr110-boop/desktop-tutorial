using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using DataLayer.Context;

namespace Services.NasbNiro.Finance
{
    /// <summary>
    /// M7 (review R1t): a treasury document (receipt, payment, petty-cash settlement, a cash-leg draft of a loan, letter of credit or guarantee) is not made or edited with a date inside a CLOSED period or a
    /// closed fiscal year: the bank book, the cash flow by date and every report of that period would change after the close. Events whose source existed before the close are still posted late by the GL
    /// (see <c>EventPostingService.ResolveDateAsync</c>); that is not a new treasury document.
    /// </summary>
    internal static class ClosedPeriodGuard
    {
        /// <summary>The Persian refusal text, or null when the date is in an open or locked period (a locked period is open to the finance managers who post the document).</summary>
        public static async Task<string> MessageAsync(IUnitOfWork uow, DateTime date, CancellationToken ct)
        {
            DateTime day = date.Date;
            FnPeriod period = await uow.Set<FnPeriod>().AsNoTracking().Include(p => p.FiscalYear).Where(p => p.StartDate <= day && p.EndDate >= day).OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
            if (period != null && (period.Status == FnPeriodStatus.Closed || period.FiscalYear.Status == FnYearStatus.Closed))
                return "تاریخ سند در دوره یا سال مالی بسته‌شده است؛ برای سند جدید خزانه تاریخ یک دوره باز را بنویسید.";
            if (period == null && await uow.Set<FnFiscalYear>().AsNoTracking().AnyAsync(y => y.Status == FnYearStatus.Closed && y.StartDate <= day && y.EndDate >= day, ct))
                return "تاریخ سند در سال مالی بسته‌شده است؛ برای سند جدید خزانه تاریخ یک دوره باز را بنویسید.";
            return null;
        }
    }
}
