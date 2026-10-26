using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Office;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo
{
    // Section 9: office. Overdue meeting decisions, letters waiting for a signature, leave requests waiting for a decision;
    // the attendance of the Persian month so far by day (counts only, never a person's record or pay).
    public sealed partial class CeoOverviewService
    {
        private async Task<CeoSectionDto> OfficeAsync(CeoContext ctx, CancellationToken ct)
        {
            CeoDashboardDto b = (await PartsAsync(ctx, ct)).Dashboard;
            int lettersWaiting = await _uow.Set<OfLetter>().AsNoTracking().CountAsync(l => !l.Deleted && l.Status == OfLetterStatus.InApproval, ct);
            int leavesWaiting = await _uow.Set<OfLeaveRequest>().AsNoTracking().CountAsync(r => !r.Deleted && r.Status == OfLeaveStatus.Submitted, ct);

            var section = new CeoSectionDto();
            section.Kpis.Add(Kpi(ctx, "overdueDecisions", b.OverdueDecisions, CeoRules.WarnIfAny(b.OverdueDecisions),
                "مصوبه‌های باز صورت‌جلسه‌های تأییدشده که موعدشان گذشته است."));
            section.Kpis.Add(Kpi(ctx, "lettersWaiting", lettersWaiting, CeoRules.ByCount(lettersWaiting, 5, 15),
                "نامه‌های صادره و داخلی که در زنجیره‌ی تأیید هستند."));
            section.Kpis.Add(Kpi(ctx, "leaveRequestsPending", leavesWaiting, CeoRules.ByCount(leavesWaiting, 5, 15),
                "ارسال‌شده و در انتظار تأیید مسئول"));

            // chart: how many people were present, absent, on leave or on mission on each working day of the Persian month so far (the drill takes the same month so far)
            DateTime monthStart = CeoTrends.MonthStart(ctx.Today);
            int dayCount = (int)(ctx.Today - monthStart).TotalDays + 1;
            var rows = await _uow.Set<OfAttendanceDay>().AsNoTracking()
                .Where(d => d.Date >= monthStart && d.Date < ctx.End && !d.NonWorkingDay)
                .GroupBy(d => new { d.Date, d.Status }).Select(g => new { g.Key.Date, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
            decimal[] Counts(OfDayStatus status)
            {
                var values = new decimal[dayCount];
                foreach (var r in rows.Where(r => r.Status == status))
                {
                    int index = (int)(r.Date.Date - monthStart).TotalDays;
                    if (index >= 0 && index < dayCount) values[index] += r.Count;
                }
                return values;
            }
            section.Charts.Add(WithDrillParams(Chart("attendanceMonth", "حضور و غیاب ماه جاری به تفکیک روز", CeoKind.StackedBar, CeoUnit.Count,
                Enumerable.Range(1, dayCount).Select(d => CeoText.Number(d)), CeoLinks.OfficeAttendance, CeoDrillRef.OfAttendanceMonth,
                "تعداد افراد در هر روز کاری " + CeoText.MonthLabel(ctx.Today) + " تا امروز؛ فقط شمارش و بدون نام و حقوق.",
                Series("حاضر", Counts(OfDayStatus.Present), CeoRole.Positive), Series("غایب", Counts(OfDayStatus.Absent), CeoRole.Negative),
                Series("مرخصی", Counts(OfDayStatus.Leave), CeoRole.Neutral), Series("ماموریت", Counts(OfDayStatus.Mission), CeoRole.Accent)), ctx.LatestMonth()));
            return section;
        }
    }
}
