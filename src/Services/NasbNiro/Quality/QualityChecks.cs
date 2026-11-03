using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
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

        /// <summary>
        /// Nonconforming stock hold (review M18): null when the document may use the goods of its lines, otherwise the Persian reason. Goods that an open NCR (status Open, or
        /// Approved but not yet executed, and no valid concession) is about cannot be issued, delivered, sent to a contractor, moved to a usable warehouse or taken back by a
        /// production receipt or return; the report's own disposition document (<paramref name="ownNcrId"/>) is the way out. A lot is held only for the quantity of the report
        /// in the warehouse the report names, so the accepted part of an inspected receipt is still released; a serial number is held wherever it is.
        /// </summary>
        public static async Task<string> HoldAsync(IUnitOfWork uow, InventoryDocumentType type, int sourceWarehouseId, WarehouseKind? destinationKind, int? ownNcrId,
            IEnumerable<HoldLine> lines, CancellationToken cancellationToken)
        {
            if (!NonconformingHoldRules.Refuses(type, destinationKind)) return null;
            List<HoldLine> list = (lines ?? Enumerable.Empty<HoldLine>()).Where(l => l != null).ToList();
            if (list.Count == 0) return null;

            List<int> itemIds = list.Select(l => l.ItemId).Distinct().ToList();
            List<string> numbers = list.Where(l => !l.LotId.HasValue && !string.IsNullOrWhiteSpace(l.LotNumber)).Select(l => l.LotNumber.Trim()).Distinct().ToList();
            List<InventoryLot> byNumber = numbers.Count == 0
                ? new List<InventoryLot>()
                : await uow.Set<InventoryLot>().Where(l => itemIds.Contains(l.ItemId) && numbers.Contains(l.LotNumber) && !l.Deleted).ToListAsync(cancellationToken);
            List<int> lotIds = list.Where(l => l.LotId.HasValue).Select(l => l.LotId.Value).Concat(byNumber.Select(l => l.Id)).Distinct().ToList();

            // the lots are named by their lot number in the answer (a line may carry only the lot id)
            Dictionary<int, string> lotNumbers = lotIds.Count == 0
                ? new Dictionary<int, string>()
                : await uow.Set<InventoryLot>().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.LotNumber, cancellationToken);

            int own = ownNcrId ?? 0;
            List<NonConformance> candidates = (await uow.Set<NonConformance>()
                .Where(n => !n.Deleted && (n.Status == NcrStatus.Open || n.Status == NcrStatus.Approved) && n.Id != own &&
                            ((n.LotId != null && lotIds.Contains(n.LotId.Value)) || (n.SerialNumbers != null && itemIds.Contains(n.ItemId))))
                .ToListAsync(cancellationToken)).Where(n => NonconformingHoldRules.Holds(n, type)).ToList();
            if (candidates.Count == 0) return null;

            Dictionary<int, string> codes = await uow.Set<InventoryItem>().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, cancellationToken);
            Func<int, string> code = id => codes.TryGetValue(id, out string c) ? c : id.ToString();
            const string tail = " تا صدور مجوز ارفاقی یا اجرای تصمیم آن (دوباره‌کاری، ضایعات، برگشت) مصرف، حواله، تحویل یا انتقال آن به انبار قابل‌مصرف مجاز نیست؛ فقط سند اجرای خود گزارش آن را جابه‌جا می‌کند.";

            // the quantity of each lot that the document takes out, and what is on hand in its warehouse
            var lotOf = new Dictionary<HoldLine, int?>();
            foreach (HoldLine l in list)
                lotOf[l] = l.LotId ?? byNumber.FirstOrDefault(x => x.ItemId == l.ItemId && string.Equals(x.LotNumber, l.LotNumber?.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
            List<int> heldLotIds = candidates.Where(c => c.LotId.HasValue && string.IsNullOrWhiteSpace(c.SerialNumbers)).Select(c => c.LotId.Value).Distinct().ToList();
            Dictionary<(int, int), decimal> onHand = new Dictionary<(int, int), decimal>();
            if (heldLotIds.Count > 0)
                foreach (var row in await uow.Set<StockBalance>().Where(b => b.WarehouseId == sourceWarehouseId && b.LotId != null && heldLotIds.Contains(b.LotId.Value))
                    .Select(b => new { b.ItemId, LotId = b.LotId.Value, b.Quantity }).ToListAsync(cancellationToken))
                    onHand[(row.ItemId, row.LotId)] = (onHand.TryGetValue((row.ItemId, row.LotId), out decimal sum) ? sum : 0m) + row.Quantity;

            foreach (HoldLine l in list)
            {
                foreach (string serial in l.Serials ?? new List<string>())
                {
                    NonConformance n = candidates.FirstOrDefault(c => c.ItemId == l.ItemId && NonconformingHoldRules.SerialsOf(c).Contains(serial.Trim(), StringComparer.OrdinalIgnoreCase));
                    if (n != null) return $"سریال {serial} ({code(l.ItemId)}) در گزارش عدم انطباق {n.Number} نگهداری می‌شود؛" + tail;
                }
                int? lotId = lotOf[l];
                if (!lotId.HasValue) continue;
                List<NonConformance> forLot = candidates.Where(c => c.LotId == lotId.Value && c.ItemId == l.ItemId && string.IsNullOrWhiteSpace(c.SerialNumbers)).ToList();
                if (forLot.Count == 0) continue;
                string lotName = lotNumbers.TryGetValue(lotId.Value, out string storedLotNumber) && !string.IsNullOrWhiteSpace(storedLotNumber) ? storedLotNumber : l.LotNumber ?? lotId.Value.ToString();
                if (!l.Outgoing) return $"بچ {lotName} کالای {code(l.ItemId)} در گزارش عدم انطباق {forLot[0].Number} نگهداری می‌شود؛" + tail;
                decimal have = onHand.TryGetValue((l.ItemId, lotId.Value), out decimal h) ? h : 0m;
                decimal requested = list.Where(x => x.Outgoing && x.ItemId == l.ItemId && lotOf[x] == lotId).Sum(x => x.Quantity);
                decimal held = forLot.Where(c => c.WarehouseId == null || c.WarehouseId == sourceWarehouseId).Sum(c => c.Quantity <= 0 ? have : c.Quantity);
                if (NonconformingHoldRules.BlocksQuantity(have, requested, held))
                    return $"بچ {lotName} کالای {code(l.ItemId)}: {held.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} از موجودی این انبار در گزارش عدم انطباق {string.Join("، ", forLot.Select(c => c.Number))} نگهداری می‌شود؛" + tail;
            }
            return null;
        }

        /// <summary>The back-date window of the company (setting MaxBackdateDays), applied to a typed record date.</summary>
        public static async Task<string> RecordDateAsync(IUnitOfWork uow, DateTime recordDate, string what, CancellationToken cancellationToken, bool allowTomorrow = true)
        {
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, cancellationToken);
            return RecordDateRules.Check(recordDate, DateTime.Today, (int)settings[QualitySettingKeys.MaxBackdateDays], what, allowTomorrow);
        }
    }
}
