using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Quality
{
    /// <summary>Whether an order needs a first article inspection and which configuration it covers.</summary>
    public sealed class FaiRequirement
    {
        public bool Required { get; set; }
        public string Trigger { get; set; }
        public string Fingerprint { get; set; }
        public string FingerprintText { get; set; }
        public int? ListId { get; set; }
        public int? ItemId { get; set; }
        public string PartNumber { get; set; }

        /// <summary>A reason the first article cannot even be started (for example a flagged step without a released characteristic list).</summary>
        public string Problem { get; set; }

        /// <summary>The routing step after which nothing runs until the FAI is approved (sequence number).</summary>
        public int? PointSequence { get; set; }
        public QcFirstArticle Satisfying { get; set; }

        /// <summary>Package G: the automatic first article was not opened because every drawing revision of the part was released before the cut-over (the note says so; the release registers the use).</summary>
        public bool Legacy { get; set; }
        public string LegacyNote { get; set; }
    }

    /// <summary>
    /// Package B (FAI): the rules that decide when a first article is needed and which operations wait for it. Used by the order release, the shop-floor start and finish and the order completion.
    /// </summary>
    public static class FirstArticleGate
    {
        public static async Task<FaiRequirement> EvaluateAsync(IUnitOfWork uow, MfProductionOrder order, IEnumerable<MfOperation> ops, CancellationToken ct)
        {
            var req = new FaiRequirement();
            Dictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(uow, ct);
            if (st[QualitySettingKeys.PkbFaiEnforce] < 1 || !order.ItemId.HasValue) return req;
            List<MfOperation> chain = MfFlow.Chain(ops ?? Enumerable.Empty<MfOperation>()).Where(o => !o.IsRework).ToList();
            List<int> templateIds = chain.Where(o => o.OperationTemplateId.HasValue).Select(o => o.OperationTemplateId.Value).Distinct().ToList();
            var flaggedTemplates = templateIds.Count == 0 ? new List<int>() : await uow.Set<MfOperationStandard>().AsNoTracking()
                .Where(s => !s.Deleted && s.FirstArticleRequired && templateIds.Contains(s.OperationTemplateId)).Select(s => s.OperationTemplateId).ToListAsync(ct);
            MfOperation flaggedOp = chain.FirstOrDefault(o => o.OperationTemplateId.HasValue && flaggedTemplates.Contains(o.OperationTemplateId.Value));
            ResolvedCharacteristics resolved = await CharacteristicResolver.ForItemAsync(uow, order.ItemId.Value, null, ct);
            // H2: a part with a drawing in force always gets its first article after a new revision; a missing list is a refusal (below), never a way out
            bool onNewRevision = st[QualitySettingKeys.PkbFaiOnNewRevision] >= 1 && resolved.HasDrawing;
            if (flaggedOp == null && !onNewRevision) return req;
            // Package G: a part whose drawing revisions were all released before the cut-over does not open a first article automatically (a flagged routing step still does); a revision released after it does
            if (flaggedOp == null)
            {
                DateTimeOffset cutOver = await global::Services.NasbNiro.GoLive.CutOver.StartAsync(uow, st, ct);
                if (!resolved.HasList && global::Services.NasbNiro.GoLive.CutOver.DrawingsAreLegacy(resolved.Drawings, cutOver))
                {
                    req.Legacy = true;
                    req.ItemId = order.ItemId;
                    req.LegacyNote = $"نمونه اول خودکار باز نشد: همه بازنگری‌های نقشه این قطعه ({resolved.DrawingNumber}، بازنگری {resolved.DrawingRevision}) پیش از آغاز کنترل پیکربندی ({(global::Services.NasbNiro.GoLive.CutOver.Text(cutOver))}) منتشر شده‌اند.";
                    return req;
                }
            }

            var item = await uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == order.ItemId.Value).Select(i => new { i.Id, i.Code, i.PartNumber }).FirstAsync(ct);
            req.ItemId = item.Id; req.PartNumber = item.PartNumber ?? item.Code;
            if (!resolved.HasList)
            {
                req.Required = true;
                req.Problem = (flaggedOp != null ? $"مرحله «{flaggedOp.Title}» نمونه اول لازم دارد" : "این قطعه نقشه منتشرشده دارد و نمونه اول لازم است") + " اما برای نقشه آن فهرست مشخصه‌های منتشرشده وجود ندارد؛ مهندسی باید فهرست مشخصه‌ها (یا فهرست خالی با دلیل) را تهیه و منتشر کند.";
                req.Trigger = "گام نمونه اول";
                return req;
            }
            PbProjectBaseline baseline = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => b.WorkOrderId == order.WorkOrderId && b.IsCurrent && !b.Deleted).OrderByDescending(b => b.Id).FirstOrDefaultAsync(ct);
            int? routingRevision = order.RoutingRevisionId == null ? null : await uow.Set<MfRoutingRevision>().AsNoTracking().Where(r => r.Id == order.RoutingRevisionId).Select(r => (int?)r.Revision).FirstOrDefaultAsync(ct);
            // M3: the list versions are part of the configuration (a version 2 of a list on the same drawing revision needs a new first article), and so is the product BOM of an order without a project baseline
            int? productBomId = baseline != null ? (int?)null : await uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ProductItemId == order.ItemId && (h.Status == PbBomStatus.Released || h.Status == PbBomStatus.Approved))
                .OrderByDescending(h => h.Id).Select(h => (int?)h.Id).FirstOrDefaultAsync(ct);
            req.Fingerprint = FaiRules.Fingerprint(resolved.Drawings.Select(d => d.RevisionId), order.RoutingRevisionId, baseline?.BomHeaderId, resolved.Lists.Select(l => l.Id), productBomId);
            req.FingerprintText = "نقشه: " + string.Join("، ", resolved.Drawings.Select(d => $"{d.Number} بازنگری {d.RevisionCode}"))
                + " | مسیر ساخت: " + (routingRevision.HasValue ? "بازنگری " + routingRevision : "—")
                + " | BOM: " + (baseline == null ? (productBomId.HasValue ? "BOM محصول " + productBomId : "—") : $"{baseline.SourceBomCode} بازنگری {baseline.SourceBomRevision}")
                + " | فهرست مشخصه‌ها: " + (resolved.Lists.Count == 0 ? "—" : string.Join("، ", resolved.Lists.Select(l => $"نسخه {l.Version}")));
            req.ListId = resolved.ListId;
            req.Trigger = flaggedOp != null ? $"مرحله «{flaggedOp.Title}» نمونه اول لازم دارد" : "اولین دستور پس از بازنگری تازه نقشه، BOM یا مسیر ساخت";
            req.Satisfying = await uow.Set<QcFirstArticle>().AsNoTracking().FirstOrDefaultAsync(f => !f.Deleted && f.ItemId == item.Id && f.Fingerprint == req.Fingerprint && f.Status == FaiStatus.Approved, ct);
            req.Required = req.Satisfying == null;
            MfOperation point = flaggedOp ?? chain.FirstOrDefault(o => o.InspectionRequirement != MfInspectionRequirement.None) ?? chain.LastOrDefault();
            req.PointSequence = point?.Sequence;
            return req;
        }

        /// <summary>
        /// At the release of an order: refuses while the configuration has a first article of another order that is still open, and opens the first article of this order. Returns the refusal text or null.
        /// </summary>
        public static async Task<string> EnsureAtReleaseAsync(IUnitOfWork uow, MfProductionOrder order, IEnumerable<MfOperation> ops, int userId, DateTimeOffset now, CancellationToken ct)
        {
            FaiRequirement req = await EvaluateAsync(uow, order, ops, ct);
            if (req.Legacy)
                await global::Services.NasbNiro.GoLive.GlLegacyRegister.NoteAsync(uow, global::Entities.Model.NasbNiro.GoLive.GlLegacyKind.FaiNotOpened, "MfProductionOrder:" + (string.IsNullOrEmpty(order.Number) ? order.Id.ToString() : order.Number), order.WorkOrderId, order.ItemId, null, order.Number, req.LegacyNote, userId, ct);
            if (!req.Required) return null;
            if (req.Problem != null) return req.Problem;
            QcFirstArticle live = await uow.Set<QcFirstArticle>().FirstOrDefaultAsync(f => !f.Deleted && f.ItemId == req.ItemId && f.Fingerprint == req.Fingerprint
                && (f.Status == FaiStatus.Open || f.Status == FaiStatus.Measured || f.Status == FaiStatus.QcAccepted), ct);
            if (live != null)
            {
                if (live.ProductionOrderId != order.Id)
                {
                    string other = live.ProductionOrderId.HasValue ? await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.Id == live.ProductionOrderId).Select(o => o.Number).FirstOrDefaultAsync(ct) : null;
                    return $"نمونه اول {live.Number} این پیکربندی{(other == null ? string.Empty : " (دستور " + other + ")")} هنوز تایید نشده است؛ تا تایید QA، دستور دیگری از این قطعه صادر نمی‌شود.";
                }
                return null;
            }
            var fai = new QcFirstArticle
            {
                Number = await InventoryNumbers.NextNumberAsync(uow, QualityNumbers.FirstArticle, "FAI", now.Date, ct), ItemId = req.ItemId.Value, PartNumber = req.PartNumber, WorkOrderId = order.WorkOrderId,
                ProductionOrderId = order.Id > 0 ? order.Id : (int?)null, Fingerprint = req.Fingerprint, FingerprintText = req.FingerprintText, CharacteristicListId = req.ListId, RoutingRevisionId = order.RoutingRevisionId,
                Trigger = req.Trigger, Status = FaiStatus.Open, CreateDate = now, ModifyDate = now, CreatorUserId = userId, ModifierUserId = userId,
            };
            uow.Set<QcFirstArticle>().Add(fai);
            return null;
        }

        /// <summary>
        /// Null when the operation may start or finish. Operations after the first article point (and the last operation when it is the point itself) wait for the approved first article.
        /// </summary>
        public static async Task<string> OperationProblemAsync(IUnitOfWork uow, MfProductionOrder order, MfOperation op, CancellationToken ct, decimal reportedQuantity = 0m)
        {
            if (order == null || op == null || op.IsRework) return null;
            List<MfOperation> ops = await uow.Set<MfOperation>().AsNoTracking().Where(o => o.OrderId == order.Id && !o.Deleted).ToListAsync(ct);
            FaiRequirement req = await EvaluateAsync(uow, order, ops, ct);
            if (!req.Required) return null;
            // M5: until the first article is approved only the first article quantity (one piece) moves on any step of the order
            if (reportedQuantity > 1m)
            {
                QcFirstArticle pending = await uow.Set<QcFirstArticle>().AsNoTracking().Where(f => !f.Deleted && f.ProductionOrderId == order.Id && f.Status != FaiStatus.Voided && f.Status != FaiStatus.Rejected).OrderByDescending(f => f.Id).FirstOrDefaultAsync(ct);
                return $"تا تایید نمونه اول{(pending == null ? string.Empty : " " + pending.Number)} فقط یک قطعه (مقدار نمونه اول) گزارش می‌شود؛ مقدار بیشتر پس از تایید QA ثبت می‌شود.";
            }
            if (!req.PointSequence.HasValue) return null;
            List<MfOperation> chain = MfFlow.Chain(ops).Where(o => !o.IsRework).ToList();
            bool last = chain.Count > 0 && chain[chain.Count - 1].Id == op.Id;
            if (op.Sequence > req.PointSequence.Value || (last && op.Sequence == req.PointSequence.Value))
            {
                QcFirstArticle own = await uow.Set<QcFirstArticle>().AsNoTracking().Where(f => !f.Deleted && f.ProductionOrderId == order.Id && f.Status != FaiStatus.Voided && f.Status != FaiStatus.Rejected)
                    .OrderByDescending(f => f.Id).FirstOrDefaultAsync(ct);
                return $"بازرسی نمونه اول{(own == null ? string.Empty : " " + own.Number)} هنوز تایید نشده است (اندازه‌گیری، پذیرش QC و تایید QA)؛ تا آن زمان عملیات {op.Sequence} و مقدار باقی‌مانده دستور شروع یا پایان نمی‌شود.";
            }
            return null;
        }
    }

    public interface IFirstArticleService
    {
        Task<ResponseDto<PagedList<FaiListDto>>> GetAsync(FaiQuery query, CancellationToken ct);
        Task<ResponseDto<FaiDto>> GetByIdAsync(int id, CancellationToken ct);
        Task<ResponseDto<FaiDto>> ForOrderAsync(int productionOrderId, CancellationToken ct);
        Task<ResponseDto<FaiDto>> StartAsync(StartFaiDto dto, CancellationToken ct);
        Task<ResponseDto<FaiDto>> MeasureAsync(MeasureFaiDto dto, CancellationToken ct);
        Task<ResponseDto<FaiDto>> ActionAsync(FaiActionDto dto, CancellationToken ct);
    }

    public class FirstArticleService : IFirstArticleService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly IInspectionService _inspections;

        public FirstArticleService(IUnitOfWork uow, IInventoryUserContext user, IInspectionService inspections)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _inspections = inspections ?? throw new ArgumentNullException(nameof(inspections));
        }

        private int Me => _user.UserId;
        private static bool IsAi => Ai.AiActing.IsActing;
        private static string Clean(string s) => QualityText.Clean(s);
        private static ResponseDto<T> Bad<T>(string m) => QualityText.Bad<T>(m);

        public const string Starters = "QC,QA,PMO,PRODUCTIONMANAGER,SHOPSUPERVISOR";

        public async Task<ResponseDto<PagedList<FaiListDto>>> GetAsync(FaiQuery query, CancellationToken ct)
        {
            query ??= new FaiQuery();
            IQueryable<QcFirstArticle> rows = _uow.Set<QcFirstArticle>().AsNoTracking().Where(f => !f.Deleted);
            if (query.ItemId.HasValue) rows = rows.Where(f => f.ItemId == query.ItemId.Value);
            if (query.ProductionOrderId.HasValue) rows = rows.Where(f => f.ProductionOrderId == query.ProductionOrderId.Value);
            if (query.Status.HasValue) rows = rows.Where(f => f.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) rows = rows.Where(f => f.Number.Contains(text) || f.PartNumber.Contains(text));
            var projection = from f in rows
                             join i in _uow.Set<InventoryItem>().AsNoTracking() on f.ItemId equals i.Id
                             join o in _uow.Set<MfProductionOrder>().AsNoTracking() on f.ProductionOrderId equals (int?)o.Id into oj
                             from o in oj.DefaultIfEmpty()
                             join w in _uow.Set<WorkOrder>().AsNoTracking() on f.WorkOrderId equals (int?)w.Id into wj
                             from w in wj.DefaultIfEmpty()
                             orderby f.Id descending
                             select new FaiListDto
                             {
                                 Id = f.Id, Number = f.Number, ItemId = f.ItemId, ItemCode = i.Code, ItemTitle = i.Title, PartNumber = f.PartNumber, ProductionOrderId = f.ProductionOrderId,
                                 ProductionOrderNumber = o.Number, WorkOrderId = f.WorkOrderId, WorkOrderCode = w.Code, Status = f.Status, Trigger = f.Trigger, CreateDate = f.CreateDate,
                             };
            return ResponseDtoHelper.Ok(await PagedList<FaiListDto>.CreateAsync(projection, query.PageNumber, query.PageSize, ct));
        }

        public async Task<ResponseDto<FaiDto>> GetByIdAsync(int id, CancellationToken ct)
        {
            QcFirstArticle f = await _uow.Set<QcFirstArticle>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (f == null) return QualityText.NotFound<FaiDto>("بازرسی نمونه اول پیدا نشد.");
            return ResponseDtoHelper.Ok(await ToDtoAsync(f, ct));
        }

        public async Task<ResponseDto<FaiDto>> ForOrderAsync(int productionOrderId, CancellationToken ct)
        {
            QcFirstArticle f = await _uow.Set<QcFirstArticle>().AsNoTracking().Where(x => !x.Deleted && x.ProductionOrderId == productionOrderId && x.Status != FaiStatus.Voided)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (f == null) return QualityText.NotFound<FaiDto>("برای این دستور تولید نمونه اولی ثبت نشده است.");
            return ResponseDtoHelper.Ok(await ToDtoAsync(f, ct));
        }

        private async Task<FaiDto> ToDtoAsync(QcFirstArticle f, CancellationToken ct)
        {
            var item = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => i.Id == f.ItemId).Select(i => new { i.Code, i.Title }).FirstAsync(ct);
            string orderNo = f.ProductionOrderId.HasValue ? await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.Id == f.ProductionOrderId).Select(o => o.Number).FirstOrDefaultAsync(ct) : null;
            string woCode = f.WorkOrderId.HasValue ? await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == f.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct) : null;
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { f.MeasuredByUserId, f.QcAcceptedByUserId, f.QaApprovedByUserId, f.RejectedByUserId }, ct);
            var dto = new FaiDto
            {
                Id = f.Id, Number = f.Number, ItemId = f.ItemId, ItemCode = item.Code, ItemTitle = item.Title, PartNumber = f.PartNumber, ProductionOrderId = f.ProductionOrderId, ProductionOrderNumber = orderNo,
                WorkOrderId = f.WorkOrderId, WorkOrderCode = woCode, Status = f.Status, Trigger = f.Trigger, CreateDate = f.CreateDate, Fingerprint = f.Fingerprint, FingerprintText = f.FingerprintText,
                CharacteristicListId = f.CharacteristicListId, QcRecordId = f.QcRecordId, MeasuredByName = names.Name(f.MeasuredByUserId), MeasuredAt = f.MeasuredAt, QcAcceptedByName = names.Name(f.QcAcceptedByUserId),
                QcAcceptedAt = f.QcAcceptedAt, QcAcceptNote = f.QcAcceptNote, QaApprovedByName = names.Name(f.QaApprovedByUserId), QaApprovedAt = f.QaApprovedAt, QaApproveNote = f.QaApproveNote,
                RejectedByName = names.Name(f.RejectedByUserId), RejectReason = f.RejectReason, VoidReason = f.VoidReason, Note = f.Note, RowVersion = f.RowVersion == null ? null : Convert.ToBase64String(f.RowVersion),
            };
            if (f.QcRecordId.HasValue) dto.QcRecordNumber = await _uow.Set<QcRecord>().AsNoTracking().Where(r => r.Id == f.QcRecordId).Select(r => r.Number).FirstOrDefaultAsync(ct);
            if (f.CharacteristicListId.HasValue)
            {
                List<QcCharacteristic> lines = await _uow.Set<QcCharacteristic>().AsNoTracking().Where(c => c.ListId == f.CharacteristicListId).OrderBy(c => c.RowNumber).ToListAsync(ct);
                List<QcMeasurement> measured = f.QcRecordId.HasValue
                    ? await _uow.Set<QcMeasurement>().AsNoTracking().Include(m => m.Equipment).Where(m => m.RecordId == f.QcRecordId).OrderBy(m => m.Id).ToListAsync(ct) : new List<QcMeasurement>();
                foreach (QcCharacteristic c in lines)
                {
                    QcMeasurement m = measured.FirstOrDefault(x => x.CharacteristicId == c.Id);
                    dto.Lines.Add(new FaiLineDto
                    {
                        CharacteristicId = c.Id, No = c.No, Name = c.Name, Nominal = c.Nominal, TolPlus = c.TolPlus, TolMinus = c.TolMinus, Lower = c.Lower, Upper = c.Upper, Unit = c.Unit,
                        ClassText = CharacteristicRules.ClassText(c.Class), InstrumentText = CharacteristicRules.InstrumentText(c.InstrumentClass), Method = c.Method,
                        Measured = m?.Measured, Pass = m?.Pass, EquipmentCode = m?.Equipment?.Code,
                    });
                }
            }
            bool qc = _user.IsInRole("QC"), qa = _user.IsInRole("QA");
            if (f.Status == FaiStatus.Open && qc) { dto.Actions.Add("measure"); dto.Actions.Add("reject"); }
            if (f.Status == FaiStatus.Measured && qc && !IsAi && f.MeasuredByUserId != Me) { dto.Actions.Add("qc-accept"); }
            if (f.Status == FaiStatus.Measured && qc) dto.Actions.Add("reject");
            if (f.Status == FaiStatus.QcAccepted && qa && !IsAi && f.MeasuredByUserId != Me && f.QcAcceptedByUserId != Me) dto.Actions.Add("qa-approve");
            if (f.Status == FaiStatus.QcAccepted && qa) dto.Actions.Add("reject");
            if ((f.Status == FaiStatus.Open || f.Status == FaiStatus.Measured || f.Status == FaiStatus.QcAccepted) && (qa || _user.IsInRole("PMO"))) dto.Actions.Add("void");
            dto.Actions = dto.Actions.Distinct().ToList();
            return dto;
        }

        public async Task<ResponseDto<FaiDto>> StartAsync(StartFaiDto dto, CancellationToken ct)
        {
            if (dto == null) return Bad<FaiDto>(QualityText.InvalidData);
            if (!_user.IsInRole(Starters)) return Bad<FaiDto>(QualityText.NotAllowed);
            MfProductionOrder order = await _uow.Set<MfProductionOrder>().FirstOrDefaultAsync(o => o.Id == dto.ProductionOrderId && !o.Deleted, ct);
            if (order == null) return QualityText.NotFound<FaiDto>("دستور تولید پیدا نشد.");
            if (order.Status == MfOrderStatus.Draft) return Bad<FaiDto>("نمونه اول پس از صدور دستور تولید باز می‌شود (هنگام صدور خودکار ساخته می‌شود).");
            List<MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => o.OrderId == order.Id && !o.Deleted).ToListAsync(ct);
            FaiRequirement req = await FirstArticleGate.EvaluateAsync(_uow, order, ops, ct);
            if (!req.Required) return Bad<FaiDto>("برای این دستور تولید نمونه اول لازم نیست یا برای همین پیکربندی نمونه اول تاییدشده وجود دارد.");
            if (req.Problem != null) return Bad<FaiDto>(req.Problem);
            if (await _uow.Set<QcFirstArticle>().AnyAsync(f => !f.Deleted && f.ItemId == req.ItemId && f.Fingerprint == req.Fingerprint
                && (f.Status == FaiStatus.Open || f.Status == FaiStatus.Measured || f.Status == FaiStatus.QcAccepted), ct))
                return Bad<FaiDto>("برای این پیکربندی نمونه اول در جریان است.");
            DateTimeOffset now = DateTimeOffset.Now;
            var fai = new QcFirstArticle
            {
                Number = await InventoryNumbers.NextNumberAsync(_uow, QualityNumbers.FirstArticle, "FAI", now.Date, ct), ItemId = req.ItemId.Value, PartNumber = req.PartNumber, WorkOrderId = order.WorkOrderId,
                ProductionOrderId = order.Id, Fingerprint = req.Fingerprint, FingerprintText = req.FingerprintText, CharacteristicListId = req.ListId, RoutingRevisionId = order.RoutingRevisionId,
                Trigger = req.Trigger, Status = FaiStatus.Open, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<QcFirstArticle>().Add(fai);
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Bad<FaiDto>("برای این پیکربندی هم‌زمان نمونه اول دیگری ساخته شد."); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(fai, ct));
        }

        public async Task<ResponseDto<FaiDto>> MeasureAsync(MeasureFaiDto dto, CancellationToken ct)
        {
            if (dto == null || dto.Measurements == null || dto.Measurements.Count == 0) return Bad<FaiDto>("مقدار اندازه‌گیری‌شده مشخصه‌ها را وارد کنید.");
            if (!_user.IsInRole("QC")) return Bad<FaiDto>(QualityText.NotAllowed + " (اندازه‌گیری نمونه اول فقط با بازرس QC است)");
            QcFirstArticle f = await _uow.Set<QcFirstArticle>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (f == null) return QualityText.NotFound<FaiDto>("بازرسی نمونه اول پیدا نشد.");
            if (f.Status != FaiStatus.Open) return Bad<FaiDto>("فقط نمونه اول باز اندازه‌گیری می‌شود.");
            // the live fingerprint must still be the one of this first article: a newer revision makes the measurement worthless
            MfProductionOrder order = f.ProductionOrderId.HasValue ? await _uow.Set<MfProductionOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == f.ProductionOrderId.Value, ct) : null;
            if (order != null)
            {
                List<MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => o.OrderId == order.Id && !o.Deleted).ToListAsync(ct);
                FaiRequirement live = await FirstArticleGate.EvaluateAsync(_uow, order, ops, ct);
                if (live.Fingerprint != null && live.Fingerprint != f.Fingerprint) return Bad<FaiDto>("نقشه، فهرست مشخصه، BOM یا مسیر ساخت بازنگری تازه دارد؛ این نمونه اول را باطل و نمونه اول جدید باز کنید.");
            }
            var rec = new SaveQcRecordDto
            {
                Type = QcRecordType.FirstArticle, WorkOrderId = f.WorkOrderId, ItemId = f.ItemId, Quantity = 1, RecordDate = dto.RecordDate, Note = Clean(dto.Note) ?? $"نمونه اول {f.Number}",
                Operation = "FAI", Measurements = dto.Measurements.Select(m => new SaveQcMeasurementDto { CharacteristicId = m.CharacteristicId, Measured = m.Measured, EquipmentId = m.EquipmentId }).ToList(),
            };
            // a first article needs every characteristic of the list measured at least once; a value outside its tolerance is a reject (and a non-conformance), not an acceptance
            ResolvedCharacteristics resolved = await CharacteristicResolver.ForItemAsync(_uow, f.ItemId, null, ct);
            if (!resolved.HasLines) return Bad<FaiDto>("فهرست مشخصه‌های منتشرشده این قطعه موجود نیست یا مشخصه‌ای ندارد.");
            bool anyFail = dto.Measurements.Any(m =>
            {
                CharLine l = resolved.Lines.FirstOrDefault(x => x.Id == m.CharacteristicId);
                return l != null && !CharacteristicRules.Pass(l, m.Measured);
            });
            rec.Result = anyFail ? QcResult.Reject : QcResult.Accept;
            if (anyFail) rec.Note = (Clean(dto.Note) ?? $"نمونه اول {f.Number}") + " (مشخصه خارج از تلرانس)";
            ResponseDto<QcRecordDto> saved = await _inspections.SaveRecordAsync(rec, ct);
            if (!saved.Success) return Bad<FaiDto>(saved.Message);
            DateTimeOffset now = DateTimeOffset.Now;
            // the inspection service saved its own changes; load our row again for the status change
            f = await _uow.Set<QcFirstArticle>().FirstAsync(x => x.Id == dto.Id, ct);
            f.QcRecordId = saved.Data.Id; f.MeasuredByUserId = Me; f.MeasuredAt = now; f.ModifyDate = now; f.ModifierUserId = Me;
            if (anyFail) { f.Status = FaiStatus.Rejected; f.RejectedByUserId = Me; f.RejectedAt = now; f.RejectReason = "مشخصه خارج از تلرانس؛ گزارش عدم انطباق " + (saved.Data.NcrNumber ?? "") + " ساخته شد."; }
            else f.Status = FaiStatus.Measured;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<FaiDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(f, ct));
        }

        public async Task<ResponseDto<FaiDto>> ActionAsync(FaiActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action)) return Bad<FaiDto>(QualityText.InvalidData);
            QcFirstArticle f = await _uow.Set<QcFirstArticle>().FirstOrDefaultAsync(x => x.Id == dto.Id && !x.Deleted, ct);
            if (f == null) return QualityText.NotFound<FaiDto>("بازرسی نمونه اول پیدا نشد.");
            if (!string.IsNullOrEmpty(dto.RowVersion))
                try { _uow.Entry(f).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(dto.RowVersion); } catch (FormatException) { return Bad<FaiDto>(QualityText.InvalidData); }
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (dto.Action.Trim().ToLowerInvariant())
            {
                case "qc-accept":
                {
                    if (!_user.IsInRole("QC")) return Bad<FaiDto>(QualityText.NotAllowed);
                    if (IsAi) return Bad<FaiDto>("پذیرش نمونه اول با انسان است؛ صندلی هوش مصنوعی آن را نمی‌پذیرد.");
                    if (f.Status != FaiStatus.Measured) return Bad<FaiDto>("فقط نمونه اول اندازه‌گیری‌شده پذیرفته می‌شود.");
                    string problem = FaiRules.SeparationProblem(f.MeasuredByUserId, null, Me, true);
                    if (problem != null) return Bad<FaiDto>(problem);
                    f.Status = FaiStatus.QcAccepted; f.QcAcceptedByUserId = Me; f.QcAcceptedAt = now; f.QcAcceptNote = note;
                    break;
                }
                case "qa-approve":
                {
                    if (!_user.IsInRole("QA")) return Bad<FaiDto>(QualityText.NotAllowed);
                    if (IsAi) return Bad<FaiDto>("تایید نمونه اول با انسان است؛ صندلی هوش مصنوعی آن را تایید نمی‌کند.");
                    if (f.Status != FaiStatus.QcAccepted) return Bad<FaiDto>("QA فقط نمونه اولِ پذیرفته‌شده توسط QC را تایید می‌کند.");
                    string problem = FaiRules.SeparationProblem(f.MeasuredByUserId, f.QcAcceptedByUserId, Me, false);
                    if (problem != null) return Bad<FaiDto>(problem);
                    // the configuration must be the one measured: a revision released in the meantime makes this first article out of date
                    MfProductionOrder order = f.ProductionOrderId.HasValue ? await _uow.Set<MfProductionOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == f.ProductionOrderId.Value, ct) : null;
                    if (order != null)
                    {
                        List<MfOperation> ops = await _uow.Set<MfOperation>().AsNoTracking().Where(o => o.OrderId == order.Id && !o.Deleted).ToListAsync(ct);
                        FaiRequirement live = await FirstArticleGate.EvaluateAsync(_uow, order, ops, ct);
                        if (live.Fingerprint != null && live.Fingerprint != f.Fingerprint) return Bad<FaiDto>("پس از اندازه‌گیری، بازنگری تازه‌ای از نقشه، فهرست مشخصه، BOM یا مسیر ساخت منتشر شده است؛ این نمونه اول را باطل و نمونه اول جدید باز کنید.");
                    }
                    f.Status = FaiStatus.Approved; f.QaApprovedByUserId = Me; f.QaApprovedAt = now; f.QaApproveNote = note;
                    break;
                }
                case "reject":
                    if (!_user.IsInRole("QC,QA")) return Bad<FaiDto>(QualityText.NotAllowed);
                    if (f.Status == FaiStatus.Approved || f.Status == FaiStatus.Rejected || f.Status == FaiStatus.Voided) return Bad<FaiDto>("این نمونه اول بسته شده است.");
                    if (note == null) return Bad<FaiDto>("دلیل رد را بنویسید.");
                    f.Status = FaiStatus.Rejected; f.RejectedByUserId = Me; f.RejectedAt = now; f.RejectReason = note;
                    break;
                case "void":
                    if (!_user.IsInRole("QA,PMO")) return Bad<FaiDto>(QualityText.NotAllowed);
                    if (f.Status == FaiStatus.Approved || f.Status == FaiStatus.Rejected || f.Status == FaiStatus.Voided) return Bad<FaiDto>("این نمونه اول بسته شده است.");
                    if (note == null) return Bad<FaiDto>("دلیل ابطال را بنویسید.");
                    f.Status = FaiStatus.Voided; f.VoidReason = note;
                    break;
                default:
                    return Bad<FaiDto>("عمل نامعتبر است (qc-accept، qa-approve، reject یا void).");
            }
            f.ModifyDate = now; f.ModifierUserId = Me;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<FaiDto>(QualityText.Concurrency); }
            return ResponseDtoHelper.Ok(await ToDtoAsync(f, ct));
        }
    }
}
