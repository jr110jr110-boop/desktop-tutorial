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
            // a late entry is for a few days (a month at most); effectiveness is judged after at least a week, never at once (R1q review M14)
            { QualitySettingKeys.MaxBackdateDays, new KeyValuePair<int, int>(0, 30) },
            { QualitySettingKeys.EffectivenessWaitDays, new KeyValuePair<int, int>(7, 365) },
            { QualitySettingKeys.MinNoticeDays, new KeyValuePair<int, int>(1, 30) },
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

        /// <summary>
        /// A stored value forced into its range (R1q review M14). Rows written before the ranges existed, or by hand in the database, must not switch a control off:
        /// a value below the minimum or above the maximum is read as the nearest bound, a fraction is cut. Keys without a range are returned unchanged.
        /// </summary>
        public static decimal Clamp(string key, decimal value)
        {
            if (!TryGetBounds(key, out int min, out int max)) return value;
            decimal whole = decimal.Truncate(value);
            return whole < min ? min : whole > max ? max : whole;
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
        public static string Check(DateTime recordDate, DateTime today, int maxBackdateDays, string what, bool allowTomorrow = true)
        {
            // one day of leeway for the time zone of the browser, as the checks of the services always had; a weld log (it restarts the six-month continuity of a welder)
            // gets none: at most today (review M15)
            if (recordDate.Date > (allowTomorrow ? today.Date.AddDays(1) : today.Date)) return $"تاریخ {what} نمی‌تواند در آینده باشد.";
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
        /// Whether the release-note gate applies to a customer delivery (review H4). It applies whenever the delivery names a work order or carries a serial number that a work
        /// order made: a sales order or a service order does not exempt it. Only spare parts against a sales order or a service order, with no work order anywhere in the
        /// delivery, are exempt (the fulfilment check of the sales order applies instead); a delivery that has no order of any kind must name its work order, so the gate applies
        /// and refuses it. The open-NCR check does not depend on this: it runs on every customer delivery.
        /// </summary>
        public static bool GateApplies(bool hasSalesOrder, bool hasServiceOrder, bool hasWorkOrder, bool anyUnitHasWorkOrder) =>
            hasWorkOrder || anyUnitHasWorkOrder || (!hasSalesOrder && !hasServiceOrder);

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

        /// <summary>Document types that use the material up. A transfer into quarantine or scrap, a supplier return, a scrap sale and an adjustment are the ways an expired lot leaves.</summary>
        public static bool Consumes(InventoryDocumentType type) =>
            type == InventoryDocumentType.Issue || type == InventoryDocumentType.CustomerDelivery ||
            type == InventoryDocumentType.SubcontractIssue || type == InventoryDocumentType.StageConversion;

        /// <summary>
        /// Whether the document refuses an expired lot (review M13): the documents above always do; a transfer (including the release from quarantine and the return from a
        /// subcontractor) moves expired goods only INTO a quarantine or scrap warehouse, never into stock that production or a customer can draw on.
        /// </summary>
        public static bool Refuses(InventoryDocumentType type, WarehouseKind? destination) =>
            Consumes(type) ||
            (global::Services.NasbNiro.Inventory.InventoryDocumentRules.IsTransfer(type) && destination != WarehouseKind.Quarantine && destination != WarehouseKind.Scrap);

        /// <summary>
        /// A concession covers the use of an expired lot only when it was approved on or after the expiry date (review M13): a permit given while the lot was still good says nothing
        /// about the lot after it spoiled.
        /// </summary>
        public static bool ConcessionCovers(DateTime expiryDate, DateTimeOffset concessionApprovedAt) => concessionApprovedAt.Date >= expiryDate.Date;
    }

    /// <summary>One line of a document as the nonconforming-stock hold looks at it (the same shape whether the document is still a request body or already a saved draft).</summary>
    public sealed class HoldLine
    {
        public int ItemId { get; set; }
        public int? LotId { get; set; }
        public string LotNumber { get; set; }
        public decimal Quantity { get; set; }
        public List<string> Serials { get; set; } = new List<string>();

        /// <summary>The goods leave the document's warehouse (an issue, a delivery, a transfer out); false = the line brings goods into stock.</summary>
        public bool Outgoing { get; set; } = true;
    }

    /// <summary>
    /// Nonconforming stock hold (review M18): goods that an open NCR is about are not issued, delivered, moved to stock that production or a customer draws on, or taken back
    /// into production until the report is decided and executed. The report's own disposition document (rework or repair into quarantine, scrap, return) is the way out.
    /// </summary>
    public static class NonconformingHoldRules
    {
        /// <summary>
        /// Documents that refuse held goods: the ones that use goods up, production returns, and transfers into any warehouse but quarantine or scrap. A production receipt is not
        /// among them (R1p D7/D33): it receives the good pieces of the last step alone — the pieces a report is about were scrapped at the terminal (written off, never in the receipt)
        /// or are reworked and accepted by QC before they count as good — so a report on the order's lot must not keep the conforming pieces out of stock.
        /// </summary>
        public static bool Refuses(InventoryDocumentType type, WarehouseKind? destination) =>
            LotExpiryRules.Consumes(type) || type == InventoryDocumentType.ProductionReturn ||
            (global::Services.NasbNiro.Inventory.InventoryDocumentRules.IsTransfer(type) && destination != WarehouseKind.Quarantine && destination != WarehouseKind.Scrap);

        /// <summary>The report holds its goods while it is open or approved but not yet executed; a valid concession permit is the permission to use them.</summary>
        public static bool Holds(NonConformance n) =>
            n != null && !n.Deleted && (n.Status == NcrStatus.Open || n.Status == NcrStatus.Approved) && !ConcessionRules.IsValid(n, false);

        /// <summary>The same rule for one kind of document: a production receipt is never held (see <see cref="Refuses"/>), every other document follows <see cref="Holds(NonConformance)"/>.</summary>
        public static bool Holds(NonConformance n, InventoryDocumentType type) => type != InventoryDocumentType.ProductionReceipt && Holds(n);

        /// <summary>The serial numbers an NCR lists, trimmed.</summary>
        public static List<string> SerialsOf(NonConformance n) =>
            (n?.SerialNumbers ?? string.Empty).Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        /// <summary>
        /// Whether an outgoing line of a lot is blocked: the reports hold <paramref name="heldQuantity"/> of the lot in the warehouse (0 = nothing is held there) and only what
        /// is left beyond that may go: <paramref name="onHand"/> less everything the document takes (<paramref name="requested"/>) must still cover the held quantity.
        /// </summary>
        public static bool BlocksQuantity(decimal onHand, decimal requested, decimal heldQuantity) =>
            heldQuantity > 0 && onHand - requested < heldQuantity;
    }

    /// <summary>
    /// The concession permit of an NCR (R1q D3/D10/D32, review H4): it counts only on a report whose decision is a concession, that all three signers signed, and whose permit was
    /// given after the quality signature. Everything that relies on "a concession exists" uses this one rule.
    /// </summary>
    public static class ConcessionRules
    {
        public static bool IsValid(NonConformance n, bool includeClosed) =>
            n != null && n.Disposition == NcrDisposition.Concession && StatusCounts(n.Status, includeClosed) &&
            n.QualityApprovedAt.HasValue && n.EngineeringApprovedAt.HasValue && n.ManufacturingApprovedAt.HasValue &&
            n.ConcessionApprovedAt.HasValue && n.ConcessionApprovedAt.Value >= n.QualityApprovedAt.Value;

        private static bool StatusCounts(NcrStatus status, bool includeClosed) =>
            status == NcrStatus.Approved || status == NcrStatus.Executed || (includeClosed && status == NcrStatus.Closed);

        /// <summary>The same rule for a database query.</summary>
        public static IQueryable<NonConformance> Valid(IQueryable<NonConformance> source, bool includeClosed) =>
            source.Where(n => !n.Deleted && n.Disposition == NcrDisposition.Concession &&
                              (n.Status == NcrStatus.Approved || n.Status == NcrStatus.Executed || (includeClosed && n.Status == NcrStatus.Closed)) &&
                              n.QualityApprovedAt != null && n.EngineeringApprovedAt != null && n.ManufacturingApprovedAt != null &&
                              n.ConcessionApprovedAt != null && n.ConcessionApprovedAt >= n.QualityApprovedAt);
    }

    /// <summary>CAPA timing rules (R1q D13 and D20).</summary>
    public static class CapaRules
    {
        /// <summary>The first day the effectiveness of an implemented CAPA may be judged.</summary>
        public static DateTime EffectivenessEarliest(DateTime implementedAt, int waitDays) => implementedAt.Date.AddDays(waitDays);

        /// <summary>The CAPA is implemented on the day its last action of the round was done (today when no day is known).</summary>
        public static DateTime ImplementedOn(IEnumerable<DateTime?> actionDoneDates, DateTime today)
        {
            DateTime? last = (actionDoneDates ?? Enumerable.Empty<DateTime?>()).Where(d => d.HasValue).Select(d => (DateTime?)d.Value.Date).DefaultIfEmpty(null).Max();
            return last ?? today.Date;
        }

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

    /// <summary>The quality gates of an item (R1q D11, review M8): reducing one needs a reason and always the second approval of QA; a stricter control applies at once.</summary>
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
        /// The notification date is the server day or the day a quoted outgoing letter was sent; a typed date other than today is refused, because a back-dated
        /// notice would let the witness be waived at once. Null when acceptable, otherwise the Persian reason.
        /// </summary>
        public static string CheckTypedDate(DateTime? typed, DateTime today) =>
            typed.HasValue && typed.Value.Date != today.Date
                ? "تاریخ اعلام را نمی‌توان دستی و عقب‌تر از امروز ثبت کرد؛ تاریخ اعلام همان زمان ثبت در سامانه است، یا نامه صادره ارسال‌شده‌ای را که اعلام را ابلاغ کرده انتخاب کنید."
                : null;

        /// <summary>An activity without a required test report is finished only with proof: an accepted inspection record on it or a document.</summary>
        public static bool HasEvidence(bool acceptedRecordExists, Guid? evidenceDocumentId) => acceptedRecordExists || evidenceDocumentId.HasValue;

        /// <summary>The longest text the reference of a not-applicable decision may hold (the column length).</summary>
        public const int MaxReferenceLength = 300;

        /// <summary>The notice period in force: the stored one, never shorter than the company minimum (plans saved before the minimum existed may hold 0).</summary>
        public static int EffectiveDays(int stored, int minimum) => Math.Max(stored, Math.Max(minimum, 0));

        /// <summary>Null when the notice period may be saved, otherwise the Persian reason (R1q review H6: a notice of 0 days would waive the client's witness at once).</summary>
        public static string CheckNoticeDays(int days, int minimum) =>
            days < minimum || days > 365 ? $"مهلت اعلام نقطه شاهد باید بین {minimum} تا 365 روز باشد." : null;

        /// <summary>
        /// A witness notice proved by a letter counts from the day the letter was SENT (not signed), never from the future, never before the client approved the plan,
        /// and not from further back than the back-date window (R1q review H5). Null when acceptable, otherwise the Persian reason.
        /// </summary>
        public static string CheckLetterDate(DateTime sent, DateTime today, DateTime? planApproval, int maxBackdateDays)
        {
            if (sent.Date > today.Date) return "تاریخ ارسال نامه در آینده است؛ فقط نامه‌ای که ارسال شده مرجع اعلام است.";
            if (planApproval.HasValue && sent.Date < planApproval.Value.Date)
                return "نامه پیش از تایید برنامه توسط کارفرما ارسال شده است و اعلام این آزمون را اثبات نمی‌کند؛ نامه اعلام را پس از تایید برنامه ارسال کنید.";
            if (sent.Date < today.Date.AddDays(-Math.Max(0, maxBackdateDays)))
                return $"تاریخ ارسال نامه بیش از {Math.Max(0, maxBackdateDays)} روز پیش است؛ برای این آزمون نامه اعلام تازه‌ای ارسال کنید.";
            return null;
        }

        /// <summary>The client's letter that agrees a control point is not applicable: not from the future, and written after the plan was last approved with that point in it (R1q review H6).</summary>
        public static string CheckClientLetterDate(DateTime letterDate, DateTime today, DateTime? lastPlanApproval)
        {
            if (letterDate.Date > today.Date) return "تاریخ نامه کارفرما در آینده است.";
            if (lastPlanApproval.HasValue && letterDate.Date < lastPlanApproval.Value.Date)
                return "نامه کارفرما پیش از آخرین تایید برنامه است؛ حذف نقطه کنترلی تاییدشده فقط با نامه‌ای که پس از تایید برنامه رسیده پذیرفته می‌شود.";
            return null;
        }

        /// <summary>The date of the client's approval of the plan: not in the future and not further back than the back-date window (R1q review H6).</summary>
        public static string CheckApprovalDate(DateTime approval, DateTime today, int maxBackdateDays)
        {
            if (approval.Date > today.Date) return "تاریخ تایید کارفرما نمی‌تواند در آینده باشد.";
            if (approval.Date < today.Date.AddDays(-Math.Max(0, maxBackdateDays)))
                return $"تاریخ تایید کارفرما بیش از {Math.Max(0, maxBackdateDays)} روز پیش است؛ تاریخ ثبت در سامانه را وارد کنید.";
            return null;
        }
    }

    /// <summary>Which external party of the Office register stands for the client of a contract (R1q review H5, H6).</summary>
    public static class ClientPartyRules
    {
        /// <summary>
        /// Never a supplier. When Sales linked customers to the contract, only a customer party that is one of them; when no link is recorded the contract does not say who
        /// its client is, so any customer or organisation party counts.
        /// </summary>
        public static bool IsClient(bool supplier, bool customer, int? customerId, IReadOnlyCollection<int> linkedCustomerIds)
        {
            if (supplier) return false;
            if (linkedCustomerIds == null || linkedCustomerIds.Count == 0) return true;
            return customer && customerId.HasValue && linkedCustomerIds.Contains(customerId.Value);
        }
    }

    /// <summary>The file that proves an inspection and test plan activity without a test report was done (R1q review M16).</summary>
    public static class EvidenceDocumentRules
    {
        /// <summary>
        /// Null when the file may serve as proof, otherwise the Persian reason. <paramref name="registered"/> is the upload moment (years before 2000 = never recorded:
        /// a file loaded before uploads were stamped), the uploader must still hold a quality or engineering role, and a file proves one activity only.
        /// </summary>
        public static string Check(DateTimeOffset registered, bool uploaderHasQualityRole, bool usedByAnotherActivity, DateTime today, int maxBackdateDays)
        {
            if (registered.Year < 2000) return "تاریخ بارگذاری این فایل ثبت نشده است (فایل قدیمی)؛ مدرک را دوباره بارگذاری کنید.";
            if (registered.LocalDateTime.Date > today.Date.AddDays(1) || registered.LocalDateTime.Date < today.Date.AddDays(-Math.Max(0, maxBackdateDays)))
                return $"فایل مدرک باید در {Math.Max(0, maxBackdateDays)} روز گذشته بارگذاری شده باشد؛ مدرک تازه بارگذاری کنید.";
            if (!uploaderHasQualityRole) return "فایل مدرک باید توسط کاربر QC، QA یا مهندسی بارگذاری شده باشد؛ آن را با کاربر مجاز دوباره بارگذاری کنید.";
            if (usedByAnotherActivity) return "این فایل پیش‌تر مدرک فعالیت دیگری شده است؛ برای هر فعالیت مدرک جداگانه بارگذاری کنید.";
            return null;
        }
    }

    /// <summary>Document revision rules (R1q D41).</summary>
    public static class RevisionRules
    {
        /// <summary>An approved revision becomes effective at once only when its date has come; otherwise it is scheduled and the current revision stays in force.</summary>
        public static bool TakesEffectNow(DateTime effectiveDate, DateTime today) => effectiveDate.Date <= today.Date;
    }
}
