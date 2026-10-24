using System;
using Entities.Model.NasbNiro.Quality;

namespace Services.NasbNiro.Quality
{
    /// <summary>Calibration status is computed from the dates, never trusted from the stored column (ISO 7.1.5).</summary>
    public static class CalibrationRules
    {
        public static EquipmentStatus Effective(MeasuringEquipment e, DateTime on, int warnDays)
        {
            if (e.Status == EquipmentStatus.OutOfService) return EquipmentStatus.OutOfService;
            if (!e.RequiresCalibration) return EquipmentStatus.NotRequired;
            if (!e.NextDue.HasValue || e.NextDue.Value.Date < on.Date) return EquipmentStatus.Overdue;
            if (e.NextDue.Value.Date <= on.Date.AddDays(warnDays)) return EquipmentStatus.DueSoon;
            return EquipmentStatus.Calibrated;
        }

        /// <summary>Null when the instrument may be used on that date, otherwise the Persian reason.</summary>
        public static string CheckUsable(MeasuringEquipment e, DateTime on, int warnDays)
        {
            switch (Effective(e, on, warnDays))
            {
                case EquipmentStatus.OutOfService:
                    return $"تجهیز اندازه‌گیری {e.Code} از رده خارج است{(string.IsNullOrWhiteSpace(e.OutOfServiceReason) ? string.Empty : " (" + e.OutOfServiceReason + ")")}.";
                case EquipmentStatus.Overdue:
                    return e.NextDue.HasValue
                        ? $"کالیبراسیون تجهیز {e.Code} در {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(e.NextDue.Value))} منقضی شده است؛ استفاده از آن مجاز نیست."
                        : $"تجهیز {e.Code} هنوز کالیبره نشده است؛ استفاده از آن مجاز نیست.";
                default:
                    return null;
            }
        }
    }

    /// <summary>Qualification validity on a date, including the ASME IX QW-322 continuity rule for welders.</summary>
    public static class QualificationRules
    {
        public static bool NeedsContinuity(QualificationKind kind) =>
            kind == QualificationKind.Welder || kind == QualificationKind.WeldingOperator;

        public static QualificationState State(Qualification q, DateTime on, DateTime? lastUse, int continuityMonths, int warnDays)
        {
            if (q.Revoked) return QualificationState.Revoked;
            if (q.Superseded) return QualificationState.Expired;
            if (q.IssuedDate.Date > on.Date) return QualificationState.Expired;
            if (q.ExpiryDate.HasValue && q.ExpiryDate.Value.Date < on.Date) return QualificationState.Expired;
            DateTime? lapseOn = null;
            if (NeedsContinuity(q.Kind))
            {
                DateTime reference = lastUse.HasValue && lastUse.Value.Date > q.IssuedDate.Date ? lastUse.Value.Date : q.IssuedDate.Date;
                lapseOn = reference.AddMonths(continuityMonths);
                if (lapseOn.Value < on.Date) return QualificationState.Lapsed;
            }
            if (q.ExpiryDate.HasValue && q.ExpiryDate.Value.Date <= on.Date.AddDays(warnDays)) return QualificationState.ExpiringSoon;
            if (lapseOn.HasValue && lapseOn.Value <= on.Date.AddDays(warnDays)) return QualificationState.ExpiringSoon;
            return QualificationState.Valid;
        }

        public static string StateText(QualificationState state)
        {
            switch (state)
            {
                case QualificationState.Revoked: return "ابطال‌شده";
                case QualificationState.Expired: return "منقضی‌شده";
                case QualificationState.Lapsed: return "تداوم جوشکاری قطع شده (بیش از شش ماه بدون جوشکاری)؛ آزمون مجدد لازم است";
                default: return "معتبر";
            }
        }

        public static bool Usable(QualificationState state) => state == QualificationState.Valid || state == QualificationState.ExpiringSoon;
    }
}
