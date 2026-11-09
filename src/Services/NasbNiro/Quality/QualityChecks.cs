using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Services.NasbNiro.Quality
{
    /// <summary>Checks that several quality services share.</summary>
    internal static class QualityChecks
    {
        /// <summary>
        /// Fix round 1 (M5): runs several saves as one unit in a database transaction (SQL Server). It uses the execution strategy of the retrying provider, so the work may run again from the start and must load what it
        /// changes itself. A result that is not a success rolls back. The in-memory database of the tests has no transactions, so the work simply runs.
        /// </summary>
        public static async Task<T> InTransactionAsync<T>(IUnitOfWork uow, Func<Task<T>> work, Func<T, bool> succeeded, CancellationToken ct)
        {
            if (!(uow is DbContext db) || !db.Database.IsRelational()) return await work();
            IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
            int attempt = 0;
            return await strategy.ExecuteAsync<T>(async token =>
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(token);
                T result = await work();
                if (succeeded(result)) await transaction.CommitAsync(token); else await transaction.RollbackAsync(token);
                return result;
            }, ct);
        }

        private static readonly string[] AllowedMimePrefixes = { "application/pdf", "image/", "application/vnd.openxmlformats-officedocument", "application/vnd.ms-excel", "application/msword", "text/csv", "text/plain" };

        /// <summary>
        /// Fix round 1 (M4): an attached file must exist in the documents store and be of an allowed type. The store keeps the extension (".pdf") of the file in its type column, older rows may hold a media type;
        /// both are judged (the extension against the list of the attachments module). Null = fine or no file.
        /// </summary>
        public static async Task<string> FileProblemAsync(IUnitOfWork uow, Guid? id, string label, CancellationToken ct)
        {
            if (!id.HasValue) return null;
            var row = await uow.Set<Entities.Model.Document>().AsNoTracking().Where(d => d.Id == id.Value).Select(d => new { d.MimType }).FirstOrDefaultAsync(ct);
            if (row == null) return $"فایل {label} در مخزن اسناد پیدا نشد؛ ابتدا آن را بارگذاری کنید.";
            string type = (row.MimType ?? string.Empty).Trim().ToLowerInvariant();
            if (type.Length == 0) return null;
            bool allowed = type.StartsWith(".") ? Attachments.AttachmentRules.AllowedExtensions.Contains(type) : AllowedMimePrefixes.Any(p => type.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            return allowed ? null : $"نوع فایل {label} ({type}) مجاز نیست؛ pdf، تصویر یا فایل اداری بارگذاری کنید.";
        }

        /// <summary>
        /// Fix round 1 (M1): the instruments of a RUN THAT IS OVER are judged by the state at the run: the calibration valid on the day of the run (not today). A calibration that ended after the run does not undo it.
        /// Null = usable on that day.
        /// </summary>
        public static async Task<string> EquipmentOnDayAsync(IUnitOfWork uow, IEnumerable<int> equipmentIds, DateTime day, CancellationToken ct)
        {
            List<int> ids = (equipmentIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0) return null;
            List<MeasuringEquipment> rows = await uow.Set<MeasuringEquipment>().AsNoTracking().Where(e => ids.Contains(e.Id) && !e.Deleted).ToListAsync(ct);
            if (rows.Count != ids.Count) return "تجهیز اندازه‌گیری انتخاب‌شده پیدا نشد.";
            foreach (MeasuringEquipment e in rows)
            {
                if (!e.RequiresCalibration) continue;
                List<CalibrationEvent> events = await uow.Set<CalibrationEvent>().AsNoTracking().Where(x => x.EquipmentId == e.Id && !x.Deleted).ToListAsync(ct);
                if (!CalibrationHistory.WasValidOn(events, day))
                    return $"تجهیز اندازه‌گیری {e.Code} در تاریخ {(global::Services.NasbNiro.Finance.FiscalCalendars.Text(day))} کالیبراسیون معتبر نداشته است.";
            }
            return null;
        }

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
