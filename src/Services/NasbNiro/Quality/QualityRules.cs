using System;
using System.Collections.Generic;
using System.Linq;
using Entities.Model.NasbNiro.Inventory;
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

    public static class CalibrationHistory
    {
        /// <summary>
        /// True when a good calibration (as-left pass) covered the day: the calibration date is on or before it and the next due date on or after it.
        /// The equipment row only keeps the latest calibration, so a record dated before it is judged by the events (R1q D15).
        /// </summary>
        public static bool WasValidOn(IEnumerable<CalibrationEvent> events, DateTime on) =>
            (events ?? Enumerable.Empty<CalibrationEvent>()).Any(x => !x.Deleted && x.AsLeftPass && x.NextDue.HasValue &&
                                                                       x.EventDate.Date <= on.Date && x.NextDue.Value.Date >= on.Date);
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

        /// <summary>
        /// Null when at least one of the qualifications can be used on <paramref name="on"/>; otherwise the Persian reason. <paramref name="role"/> names the
        /// qualification in the message ("بازرس"). Used for the inspector competence check (R1q D17).
        /// </summary>
        public static string CheckHeld(IEnumerable<Qualification> qualifications, DateTime on, int continuityMonths, string role)
        {
            List<Qualification> list = (qualifications ?? Enumerable.Empty<Qualification>()).ToList();
            if (list.Count == 0) return $"گواهی صلاحیت «{role}» برای شما در فهرست صلاحیت‌ها ثبت نشده است.";
            if (list.Any(q => Usable(State(q, on, null, continuityMonths, 0)))) return null;
            QualificationState latest = State(list.OrderByDescending(q => q.IssuedDate).First(), on, null, continuityMonths, 0);
            return $"گواهی صلاحیت «{role}» شما در تاریخ {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(on))} معتبر نیست: {StateText(latest)}.";
        }
    }

    /// <summary>The range each company setting may take (R1q D30): a typo must not turn a control off (welder continuity of 3650 months, a committee of nobody).</summary>
    public static class QualitySettingRules
    {
        private static readonly Dictionary<string, KeyValuePair<int, int>> Table = new Dictionary<string, KeyValuePair<int, int>>
        {
            { QualitySettingKeys.NcrMinorDays, new KeyValuePair<int, int>(1, 180) },
            { QualitySettingKeys.NcrMajorDays, new KeyValuePair<int, int>(1, 180) },
            { QualitySettingKeys.CapaEffectivenessDays, new KeyValuePair<int, int>(1, 730) },
            { QualitySettingKeys.ComplaintResponseDays, new KeyValuePair<int, int>(1, 90) },
            { QualitySettingKeys.NoticeDaysIran, new KeyValuePair<int, int>(1, 90) },
            { QualitySettingKeys.NoticeDaysAbroad, new KeyValuePair<int, int>(1, 180) },
            // ASME IX QW-322: a welder who did not weld in a process for six months loses the qualification; the company may only be stricter or equal within the year
            { QualitySettingKeys.WeldContinuityMonths, new KeyValuePair<int, int>(1, 12) },
            { QualitySettingKeys.CalibrationWarnDays, new KeyValuePair<int, int>(1, 180) },
            { QualitySettingKeys.ContinuityWarnDays, new KeyValuePair<int, int>(1, 180) },
            // a technical committee is more than one person
            { QualitySettingKeys.MajorNcrCommitteeMin, new KeyValuePair<int, int>(2, 10) },
            { QualitySettingKeys.MaxBackdateDays, new KeyValuePair<int, int>(0, 365) },
            { QualitySettingKeys.EffectivenessWaitDays, new KeyValuePair<int, int>(0, 365) },
            { QualitySettingKeys.IncomingNcrDueDays, new KeyValuePair<int, int>(1, 30) },
            { QualitySettingKeys.CapaCeoEscalationDays, new KeyValuePair<int, int>(1, 90) },
            { QualitySettingKeys.ExpiryWarnDays, new KeyValuePair<int, int>(1, 365) },
        };

        public static bool TryGetBounds(string key, out int min, out int max)
        {
            if (key != null && Table.TryGetValue(key, out KeyValuePair<int, int> range)) { min = range.Key; max = range.Value; return true; }
            min = 0; max = 0;
            return false;
        }

        /// <summary>Null when the value is acceptable, otherwise the Persian reason.</summary>
        public static string Check(string key, string title, decimal value)
        {
            if (!TryGetBounds(key, out int min, out int max)) return "تنظیم نامعتبر است.";
            if (value != decimal.Truncate(value) || value < min || value > max)
                return $"مقدار «{title}» باید عدد صحیح بین {min} تا {max} باشد.";
            return null;
        }
    }

    /// <summary>How far back a record may be dated (R1q D15): late entry is allowed for a few days, not for months.</summary>
    public static class RecordDateRules
    {
        /// <summary>Null when the date is acceptable on <paramref name="today"/>, otherwise the Persian reason; <paramref name="what"/> names the record ("بازرسی", "گزارش آزمون").</summary>
        public static string Check(DateTime recordDate, DateTime today, int maxBackdateDays, string what)
        {
            // one day of leeway for the time zone of the browser, as the checks of the services always had
            if (recordDate.Date > today.Date.AddDays(1)) return $"تاریخ {what} نمی‌تواند در آینده باشد.";
            int late = (int)(today.Date - recordDate.Date).TotalDays;
            if (late > maxBackdateDays)
                return $"تاریخ {what} {late} روز قبل است؛ ثبت با تاخیر بیش از {maxBackdateDays} روز مجاز نیست (تنظیم «حداکثر تاخیر ثبت»). برای ثبت دیرتر، QA مهلت را در تنظیمات کیفیت تغییر دهد.";
            return null;
        }
    }

    /// <summary>One line of a customer delivery or of a release note, for the coverage rule below.</summary>
    public sealed class CoverageLine
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; }
        public decimal Quantity { get; set; }
        public bool IsSerial { get; set; }

        /// <summary>Serial numbers of a serial item.</summary>
        public List<string> Serials { get; set; } = new List<string>();
    }

    /// <summary>The delivery gate (R1q D10): a customer delivery may carry only what a released release note lists.</summary>
    public static class ReleaseCoverage
    {
        /// <summary>
        /// Whether the release-note gate applies to a customer delivery. Every delivery needs a released note of its work order, except spare parts against a sales order (the
        /// fulfilment check of the sales order applies) and spare parts against a service order that has no work order; a service order that names a work order is a delivery of the
        /// work order's product and is gated like any other.
        /// </summary>
        public static bool GateApplies(bool hasSalesOrder, bool hasServiceOrder, bool hasWorkOrder) => !hasSalesOrder && (!hasServiceOrder || hasWorkOrder);

        /// <summary>
        /// The problems of the delivery lines (empty = covered). <paramref name="released"/> are the lines of the released notes of the work order,
        /// <paramref name="alreadyDelivered"/> the quantity per item that earlier confirmed deliveries of the work order took.
        /// </summary>
        public static List<string> Uncovered(IEnumerable<CoverageLine> delivery, IEnumerable<CoverageLine> released, IDictionary<int, decimal> alreadyDelivered)
        {
            var problems = new List<string>();
            List<CoverageLine> notes = (released ?? Enumerable.Empty<CoverageLine>()).ToList();
            foreach (IGrouping<int, CoverageLine> group in (delivery ?? Enumerable.Empty<CoverageLine>()).GroupBy(l => l.ItemId))
            {
                string code = group.First().ItemCode ?? group.Key.ToString();
                List<CoverageLine> forItem = notes.Where(n => n.ItemId == group.Key).ToList();
                if (forItem.Count == 0)
                {
                    problems.Add($"کالای {code} در برگه ترخیص‌شده این دستور کار نیست.");
                    continue;
                }
                if (group.Any(l => l.IsSerial))
                {
                    var allowed = new HashSet<string>(forItem.SelectMany(n => n.Serials).Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
                    foreach (string serial in group.SelectMany(l => l.Serials).Select(x => x.Trim()).Where(x => x.Length > 0))
                        if (!allowed.Contains(serial)) problems.Add($"سریال {serial} ({code}) در برگه ترخیص‌شده نیست.");
                    continue;
                }
                decimal releasedQty = forItem.Sum(n => n.Quantity);
                decimal before = alreadyDelivered != null && alreadyDelivered.TryGetValue(group.Key, out decimal done) ? done : 0m;
                decimal now = group.Sum(l => l.Quantity);
                if (before + now > releasedQty)
                    problems.Add($"مقدار تحویل کالای {code} ({FormatNumber(before + now)}) از مقدار ترخیص‌شده ({FormatNumber(releasedQty)}) بیشتر است.");
            }
            return problems;
        }

        private static string FormatNumber(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Expiry of a lot (R1q D32): an expired lot is not issued to production, to a customer or to a subcontractor unless an approved concession NCR on that lot allows it.</summary>
    public static class LotExpiryRules
    {
        public static bool IsExpired(DateTime? expiryDate, DateTime on) => expiryDate.HasValue && expiryDate.Value.Date < on.Date;

        /// <summary>Document types that use the material up. A transfer (to quarantine or scrap), a supplier return, a scrap sale and an adjustment are the ways an expired lot leaves.</summary>
        public static bool Consumes(InventoryDocumentType type) =>
            type == InventoryDocumentType.Issue || type == InventoryDocumentType.CustomerDelivery ||
            type == InventoryDocumentType.SubcontractIssue || type == InventoryDocumentType.StageConversion;
    }

    /// <summary>CAPA timing rules (R1q D13 and D20).</summary>
    public static class CapaRules
    {
        /// <summary>The first day the effectiveness of an implemented CAPA may be judged.</summary>
        public static DateTime EffectivenessEarliest(DateTime implementedAt, int waitDays) => implementedAt.Date.AddDays(waitDays);

        /// <summary>0 = on time, 1 = late (QA is told), 2 = late by at least <paramref name="ceoAfterDays"/> days (the CEO is told as well).</summary>
        public static int EscalationLevel(DateTime? due, DateTime today, int ceoAfterDays)
        {
            if (!due.HasValue || due.Value.Date >= today.Date) return 0;
            return (today.Date - due.Value.Date).TotalDays >= ceoAfterDays ? 2 : 1;
        }

        /// <summary>A CAPA the system or a rule required (drafted for a major NCR, an audit finding or a decision): only QA may cancel it, with a reason.</summary>
        public static bool IsMandatory(bool autoDrafted, CapaSource source) =>
            autoDrafted || source == CapaSource.AuditFinding || source == CapaSource.Ncr;
    }

    /// <summary>The quality gates of an item (R1q D11): reducing one needs a reason and, with open purchase orders, a second approval of QA.</summary>
    public static class ItemQualityRules
    {
        public static int? NormalizeShelfLife(int? value) => value.HasValue && value.Value > 0 ? value : null;

        /// <summary>
        /// The incoming inspection a new item starts with (WI-WH-002): required, except for group 05 (consumables and office supplies), which is received without inspection.
        /// Creating an item with the gate of its group is not a change of a quality gate; any other start value is.
        /// </summary>
        public static bool DefaultInspection(string groupCode) => groupCode != "05";

        /// <summary>Inspection on receipt switched off, the required certificate removed or lowered (EN 10204: 2.2 &lt; 3.1 &lt; 3.2), or the shelf life removed or lengthened.</summary>
        public static bool IsWeakening(bool oldInspection, CertificateType? oldCertificate, int? oldShelfLife, bool newInspection, CertificateType? newCertificate, int? newShelfLife) =>
            (oldInspection && !newInspection) ||
            (oldCertificate.HasValue && (!newCertificate.HasValue || newCertificate.Value < oldCertificate.Value)) ||
            (NormalizeShelfLife(oldShelfLife).HasValue && (!NormalizeShelfLife(newShelfLife).HasValue || NormalizeShelfLife(newShelfLife).Value > NormalizeShelfLife(oldShelfLife).Value));
    }

    /// <summary>Witness notice and not-applicable rules of the ITP (R1q D9, D18).</summary>
    public static class ItpNoticeRules
    {
        /// <summary>Notification date + notice days.</summary>
        public static DateTime? Deadline(DateTime? notificationSentAt, int noticeDays) => notificationSentAt?.Date.AddDays(noticeDays);

        /// <summary>A witness point is waived by notice only when a notice was given and its whole period has passed on <paramref name="today"/>.</summary>
        public static bool WaivedByNotice(DateTime? notificationSentAt, int noticeDays, DateTime today)
        {
            DateTime? deadline = Deadline(notificationSentAt, noticeDays);
            return deadline.HasValue && deadline.Value <= today.Date;
        }

        /// <summary>True when the client or the third-party inspector holds or witnesses the activity: only they can agree it is not applicable.</summary>
        public static bool ThirdPartyControlled(ItpPoint tpi, ItpPoint client) =>
            tpi == ItpPoint.H || tpi == ItpPoint.W || client == ItpPoint.H || client == ItpPoint.W;

        /// <summary>
        /// The notification date is the server day or the signing day of a quoted letter; a typed date other than today is refused, because a back-dated
        /// notice would let the witness be waived at once. Null when acceptable, otherwise the Persian reason.
        /// </summary>
        public static string CheckTypedDate(DateTime? typed, DateTime today) =>
            typed.HasValue && typed.Value.Date != today.Date
                ? "تاریخ اعلام را نمی‌توان دستی و عقب‌تر از امروز ثبت کرد؛ تاریخ اعلام همان زمان ثبت در سامانه است، یا نامه خروجی امضاشده‌ای را که اعلام را ابلاغ کرده انتخاب کنید."
                : null;

        /// <summary>An activity without a required test report is finished only with proof: an accepted inspection record on it or a document.</summary>
        public static bool HasEvidence(bool acceptedRecordExists, Guid? evidenceDocumentId) => acceptedRecordExists || evidenceDocumentId.HasValue;
    }

    /// <summary>Document revision rules (R1q D41).</summary>
    public static class RevisionRules
    {
        /// <summary>An approved revision becomes effective at once only when its date has come; otherwise it is scheduled and the current revision stays in force.</summary>
        public static bool TakesEffectNow(DateTime effectiveDate, DateTime today) => effectiveDate.Date <= today.Date;
    }
}
