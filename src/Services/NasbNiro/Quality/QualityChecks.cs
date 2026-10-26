using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Quality
{
    /// <summary>Checks that several quality services share.</summary>
    internal static class QualityChecks
    {
        /// <summary>
        /// The instruments may be used for a record dated <paramref name="recordDate"/> that is entered today (R1q D15): the calibration must be valid
        /// on the day of entry and, for a back-dated record, on the record date as well. Null = usable, otherwise the Persian reason.
        /// </summary>
        public static async Task<string> EquipmentAsync(IUnitOfWork uow, IEnumerable<int> equipmentIds, DateTime recordDate, CancellationToken cancellationToken)
        {
            List<int> ids = (equipmentIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0) return null;
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, cancellationToken);
            int warn = (int)settings[QualitySettingKeys.CalibrationWarnDays];
            List<MeasuringEquipment> rows = await uow.Set<MeasuringEquipment>().Where(e => ids.Contains(e.Id) && !e.Deleted).ToListAsync(cancellationToken);
            if (rows.Count != ids.Count) return "تجهیز اندازه‌گیری انتخاب‌شده پیدا نشد.";
            DateTime today = DateTime.Today;
            foreach (MeasuringEquipment e in rows)
            {
                string problem = CalibrationRules.CheckUsable(e, today, warn);
                if (problem != null) return problem;
                if (recordDate.Date > today)
                {
                    problem = CalibrationRules.CheckUsable(e, recordDate, warn);
                    if (problem != null) return problem;
                }
                else if (recordDate.Date < today && e.RequiresCalibration)
                {
                    List<CalibrationEvent> events = await uow.Set<CalibrationEvent>().Where(x => x.EquipmentId == e.Id && !x.Deleted).ToListAsync(cancellationToken);
                    if (!CalibrationHistory.WasValidOn(events, recordDate))
                        return $"تجهیز اندازه‌گیری {e.Code} در تاریخ {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(recordDate))} کالیبراسیون معتبر نداشته است؛ ثبت با آن مجاز نیست.";
                }
            }
            return null;
        }

        /// <summary>
        /// R1q D17 ★: once the company keeps "Inspector" qualifications, a QC inspector records an inspection or marks an ITP activity done only with a valid one of his
        /// own, on the record date and on the entry day. Before any is registered the register is not in use and nothing is refused. Null = allowed, otherwise the
        /// Persian reason followed by <paramref name="refusal"/>, which names what is refused.
        /// </summary>
        public static async Task<string> InspectorAsync(IUnitOfWork uow, int userId, DateTime recordDate, string refusal, CancellationToken cancellationToken)
        {
            bool registerInUse = await uow.Set<Qualification>().AnyAsync(q => q.Kind == QualificationKind.Inspector && !q.Deleted, cancellationToken);
            if (!registerInUse) return null;
            List<Qualification> mine = await uow.Set<Qualification>().Include(q => q.Person)
                .Where(q => q.Kind == QualificationKind.Inspector && !q.Deleted && q.Person.UserId == userId).ToListAsync(cancellationToken);
            string problem = QualificationRules.CheckHeld(mine, DateTime.Today, 6, "بازرس");
            if (problem == null && recordDate.Date != DateTime.Today) problem = QualificationRules.CheckHeld(mine, recordDate, 6, "بازرس");
            return problem == null ? null : problem + " " + refusal;
        }

        /// <summary>The back-date window of the company (setting MaxBackdateDays), applied to a typed record date.</summary>
        public static async Task<string> RecordDateAsync(IUnitOfWork uow, DateTime recordDate, string what, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, cancellationToken);
            return RecordDateRules.Check(recordDate, DateTime.Today, (int)settings[QualitySettingKeys.MaxBackdateDays], what);
        }
    }
}
