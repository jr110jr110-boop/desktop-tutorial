using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Trace
{
    public interface IGenealogyService
    {
        /// <summary>Links of one production order (inputs that went into what the order made).</summary>
        Task<ResponseDto<List<GenealogyLinkDto>>> ForOrderAsync(int orderId, CancellationToken ct);

        /// <summary>Rebuilds the links of one order from its confirmed issues and receipt (idempotent).</summary>
        Task<ResponseDto<GenealogySyncDto>> RebuildOrderAsync(int orderId, CancellationToken ct);

        /// <summary>Rebuilds the links of every order that has a production receipt (once after the upgrade; idempotent). Reports what could not be linked.</summary>
        Task<ResponseDto<GenealogySyncDto>> BackfillAsync(CancellationToken ct);
    }

    /// <summary>
    /// PB4 manufacturing genealogy. The inputs of a production order are the lots and serial pieces that its kit issues took out of the stock; the outputs are the lots and serial pieces of its production receipt.
    /// The link rows are DERIVED from those confirmed documents: <see cref="Sync"/> compares what the documents say with the rows and adds, updates or removes rows, so running it twice changes nothing.
    /// </summary>
    public static class GenealogyBuilder
    {
        private sealed class OutputKey
        {
            public int ItemId;
            public int? LotId;
            public int? SerialUnitId;
        }

        private sealed class InputKey
        {
            public int ItemId;
            public int? LotId;
            public int? SerialUnitId;
            public decimal Quantity;
            public int? FirstIssueId;
            public int IssueCount;
        }

        /// <summary>
        /// Brings the link rows of one order in step with its documents. Only adds to the unit of work (the caller saves). Returns what changed and counts the reasons an order could not be (fully) linked.
        /// </summary>
        public static async Task<GenealogySyncDto> SyncAsync(IUnitOfWork uow, int userId, int orderId, GenealogySource source, CancellationToken ct)
        {
            var result = new GenealogySyncDto { OrdersSeen = 1 };
            var order = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.Id == orderId && !o.Deleted)
                .Select(o => new { o.Id, o.ItemId, o.KitRequestId, o.ProductionReceiptId }).FirstOrDefaultAsync(ct);
            if (order == null) return result;

            List<GenealogyLink> existing = await uow.Set<GenealogyLink>().Where(l => l.ProductionOrderId == orderId && !l.Deleted).ToListAsync(ct);
            DateTimeOffset now = DateTimeOffset.Now;
            var desired = new List<GenealogyLink>();

            if (!order.KitRequestId.HasValue) result.OrdersWithoutKit++;
            var receipt = order.ProductionReceiptId.HasValue
                ? await uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.Id == order.ProductionReceiptId.Value && !d.Deleted).Select(d => new { d.Id, d.Status, d.DocumentDate }).FirstOrDefaultAsync(ct)
                : null;
            if (receipt == null || receipt.Status != InventoryDocumentStatus.Confirmed)
            {
                if (order.ProductionReceiptId.HasValue) result.OrdersReceiptNotConfirmed++;
            }
            else if (order.KitRequestId.HasValue)
            {
                // ----- outputs: what the receipt put into the stock
                var outRows = await uow.Set<StockTransaction>().AsNoTracking().Where(t => t.DocumentId == receipt.Id && t.Quantity > 0 && !t.IsReversal)
                    .Select(t => new { t.ItemId, t.LotId, t.SerialUnitId }).ToListAsync(ct);
                List<OutputKey> outputs = outRows.Select(r => new OutputKey { ItemId = r.ItemId, LotId = r.LotId, SerialUnitId = r.SerialUnitId })
                    .GroupBy(r => (r.ItemId, r.LotId, r.SerialUnitId)).Select(g => g.First()).ToList();
                if (outputs.Any(o => !o.LotId.HasValue && !o.SerialUnitId.HasValue)) result.OutputsWithoutTracking++;

                // ----- inputs: the confirmed issues (and returns) made against the kit of the order
                var documents = await uow.Set<InventoryDocument>().AsNoTracking().Where(d => d.MaterialRequestId == order.KitRequestId && d.Status == InventoryDocumentStatus.Confirmed && !d.Deleted
                        && (d.Type == InventoryDocumentType.Issue || d.Type == InventoryDocumentType.ProductionReturn))
                    .Select(d => new { d.Id, d.Type }).ToListAsync(ct);
                var inputs = new Dictionary<(int, int?, int?), InputKey>();
                if (documents.Count > 0)
                {
                    List<int> docIds = documents.Select(d => d.Id).ToList();
                    var moves = await uow.Set<StockTransaction>().AsNoTracking().Where(t => docIds.Contains(t.DocumentId) && !t.IsReversal)
                        .Select(t => new { t.DocumentId, t.ItemId, t.LotId, t.SerialUnitId, t.Quantity }).ToListAsync(ct);
                    Dictionary<int, InventoryDocumentType> types = documents.ToDictionary(d => d.Id, d => d.Type);
                    foreach (var m in moves.OrderBy(m => m.DocumentId))
                    {
                        var key = (m.ItemId, m.LotId, m.SerialUnitId);
                        if (!inputs.TryGetValue(key, out InputKey input)) inputs[key] = input = new InputKey { ItemId = m.ItemId, LotId = m.LotId, SerialUnitId = m.SerialUnitId };
                        // an issue has negative movements; a return from production brings the quantity back
                        input.Quantity += -m.Quantity;
                        if (types[m.DocumentId] == InventoryDocumentType.Issue)
                        {
                            input.FirstIssueId ??= m.DocumentId;
                            input.IssueCount++;
                        }
                    }
                }
                List<InputKey> consumed = inputs.Values.Where(i => i.Quantity > 0).ToList();
                if (consumed.Count == 0) result.OrdersWithoutIssuedMaterial++;
                if (consumed.Any(i => !i.LotId.HasValue && !i.SerialUnitId.HasValue)) result.InputsWithoutTracking++;

                int? operationId = await uow.Set<MfOperation>().AsNoTracking().Where(o => o.OrderId == orderId && !o.Deleted && !o.IsRework)
                    .OrderBy(o => o.Sequence).ThenBy(o => o.Id).Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);

                int shareCount = outputs.Count(o => o.SerialUnitId.HasValue);
                foreach (OutputKey output in outputs)
                {
                    bool shared = output.SerialUnitId.HasValue && shareCount > 1;
                    foreach (InputKey input in consumed)
                    {
                        decimal quantity = shared ? Math.Round(input.Quantity / shareCount, 4) : input.Quantity;
                        desired.Add(new GenealogyLink
                        {
                            ProductionOrderId = orderId, OperationId = operationId,
                            OutputItemId = output.ItemId, OutputLotId = output.LotId, OutputSerialUnitId = output.SerialUnitId,
                            InputItemId = input.ItemId, InputLotId = input.LotId, InputSerialUnitId = input.SerialUnitId,
                            Quantity = quantity, IsShared = shared, IssueDocumentId = input.FirstIssueId, ReceiptDocumentId = receipt.Id,
                            LinkDate = receipt.DocumentDate, Source = source,
                            Note = input.IssueCount > 1 ? $"{input.IssueCount} حواله" : null,
                        });
                    }
                }
            }

            // ----- reconcile
            var used = new HashSet<int>();
            foreach (GenealogyLink want in desired)
            {
                GenealogyLink have = existing.FirstOrDefault(e => !used.Contains(e.Id) && e.OutputLotId == want.OutputLotId && e.OutputSerialUnitId == want.OutputSerialUnitId
                    && e.OutputItemId == want.OutputItemId && e.InputItemId == want.InputItemId && e.InputLotId == want.InputLotId && e.InputSerialUnitId == want.InputSerialUnitId);
                if (have == null)
                {
                    want.CreateDate = now; want.ModifyDate = now; want.CreatorUserId = userId; want.ModifierUserId = userId;
                    uow.Set<GenealogyLink>().Add(want);
                    result.LinksCreated++;
                    continue;
                }
                used.Add(have.Id);
                if (have.Quantity != want.Quantity || have.IsShared != want.IsShared || have.IssueDocumentId != want.IssueDocumentId || have.OperationId != want.OperationId
                    || have.ReceiptDocumentId != want.ReceiptDocumentId || have.Note != want.Note)
                {
                    have.Quantity = want.Quantity; have.IsShared = want.IsShared; have.IssueDocumentId = want.IssueDocumentId; have.OperationId = want.OperationId;
                    have.ReceiptDocumentId = want.ReceiptDocumentId; have.Note = want.Note; have.LinkDate = want.LinkDate;
                    have.ModifyDate = now; have.ModifierUserId = userId;
                    result.LinksUpdated++;
                }
            }
            foreach (GenealogyLink gone in existing.Where(e => !used.Contains(e.Id)))
            {
                gone.Delete();
                gone.ModifyDate = now; gone.ModifierUserId = userId;
                result.LinksRemoved++;
            }
            if (desired.Count > 0) result.OrdersLinked++;
            return result;
        }

        /// <summary>
        /// Called by the warehouse after a confirmed (or cancelled) document is saved: a production receipt, or an issue or return against a kit. It finds the order and brings its links up to date.
        /// The links are derived data that can be rebuilt, so a failure here never fails the confirmation; it is swallowed and the entries it added are dropped.
        /// </summary>
        public static async Task AfterDocumentAsync(IUnitOfWork uow, int userId, InventoryDocumentType type, int documentId, int? materialRequestId, CancellationToken ct)
        {
            try
            {
                var orderIds = new List<int>();
                if (type == InventoryDocumentType.ProductionReceipt)
                    orderIds = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.ProductionReceiptId == documentId && !o.Deleted).Select(o => o.Id).ToListAsync(ct);
                else if ((type == InventoryDocumentType.Issue || type == InventoryDocumentType.ProductionReturn) && materialRequestId.HasValue)
                    orderIds = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.KitRequestId == materialRequestId && o.ProductionReceiptId != null && !o.Deleted).Select(o => o.Id).ToListAsync(ct);
                if (orderIds.Count == 0) return;
                foreach (int id in orderIds) await SyncAsync(uow, userId, id, GenealogySource.Live, ct);
                await uow.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateException || ex is InvalidOperationException)
            {
                foreach (GenealogyLink added in uow.Set<GenealogyLink>().Local.Where(l => l.Id == 0).ToList()) uow.Entry(added).State = EntityState.Detached;
            }
        }
    }

    public sealed class GenealogyService : IGenealogyService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;

        public GenealogyService(IUnitOfWork uow, IInventoryUserContext user)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public async Task<ResponseDto<List<GenealogyLinkDto>>> ForOrderAsync(int orderId, CancellationToken ct)
        {
            string number = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => o.Id == orderId && !o.Deleted).Select(o => o.Number).FirstOrDefaultAsync(ct);
            if (number == null) return ResponseDtoHelper.NotFound<List<GenealogyLinkDto>>("دستور تولید پیدا نشد.");
            List<GenealogyLinkDto> list = await _uow.Set<GenealogyLink>().AsNoTracking().Where(l => l.ProductionOrderId == orderId && !l.Deleted).OrderBy(l => l.Id)
                .Select(l => new GenealogyLinkDto
                {
                    Id = l.Id, ProductionOrderId = l.ProductionOrderId, OrderNumber = l.ProductionOrder.Number, OperationId = l.OperationId, OperationTitle = l.Operation.Title,
                    OutputItemId = l.OutputItemId, OutputItemCode = l.OutputItem.Code, OutputItemTitle = l.OutputItem.Title,
                    OutputLotId = l.OutputLotId, OutputLotNumber = l.OutputLot.LotNumber, OutputSerialUnitId = l.OutputSerialUnitId, OutputSerialNumber = l.OutputSerialUnit.SerialNumber,
                    InputItemId = l.InputItemId, InputItemCode = l.InputItem.Code, InputItemTitle = l.InputItem.Title,
                    InputLotId = l.InputLotId, InputLotNumber = l.InputLot.LotNumber, InputHeatNumber = l.InputLot.HeatNumber, InputCertificateNumber = l.InputLot.CertificateNumber,
                    InputSerialUnitId = l.InputSerialUnitId, InputSerialNumber = l.InputSerialUnit.SerialNumber,
                    Quantity = l.Quantity, IsShared = l.IsShared, IssueDocumentId = l.IssueDocumentId, IssueDocumentNumber = l.IssueDocument.Number,
                    ReceiptDocumentId = l.ReceiptDocumentId, ReceiptDocumentNumber = l.ReceiptDocument.Number, LinkDate = l.LinkDate, Source = (int)l.Source, Note = l.Note,
                }).ToListAsync(ct);
            return ResponseDtoHelper.Ok(list);
        }

        public async Task<ResponseDto<GenealogySyncDto>> RebuildOrderAsync(int orderId, CancellationToken ct)
        {
            if (Ai.AiActing.IsActing) return ResponseDtoHelper.Forbidden<GenealogySyncDto>("صندلی هوش مصنوعی پیوند شجره را بازسازی نمی‌کند.");
            if (!await _uow.Set<MfProductionOrder>().AnyAsync(o => o.Id == orderId && !o.Deleted, ct)) return ResponseDtoHelper.NotFound<GenealogySyncDto>("دستور تولید پیدا نشد.");
            GenealogySyncDto result = await GenealogyBuilder.SyncAsync(_uow, _user.UserId, orderId, GenealogySource.Backfill, ct);
            await _uow.SaveChangesAsync(ct);
            return ResponseDtoHelper.Ok(result);
        }

        public async Task<ResponseDto<GenealogySyncDto>> BackfillAsync(CancellationToken ct)
        {
            if (Ai.AiActing.IsActing) return ResponseDtoHelper.Forbidden<GenealogySyncDto>("صندلی هوش مصنوعی پیوند شجره را بازسازی نمی‌کند.");
            var total = new GenealogySyncDto();
            int after = 0;
            while (true)
            {
                List<int> ids = await _uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && o.ProductionReceiptId != null && o.Id > after).OrderBy(o => o.Id).Select(o => o.Id).Take(100).ToListAsync(ct);
                if (ids.Count == 0) break;
                foreach (int id in ids)
                {
                    GenealogySyncDto one = await GenealogyBuilder.SyncAsync(_uow, _user.UserId, id, GenealogySource.Backfill, ct);
                    total.OrdersSeen += one.OrdersSeen; total.OrdersLinked += one.OrdersLinked; total.LinksCreated += one.LinksCreated; total.LinksUpdated += one.LinksUpdated;
                    total.LinksRemoved += one.LinksRemoved; total.OrdersWithoutKit += one.OrdersWithoutKit; total.OrdersReceiptNotConfirmed += one.OrdersReceiptNotConfirmed;
                    total.OrdersWithoutIssuedMaterial += one.OrdersWithoutIssuedMaterial; total.InputsWithoutTracking += one.InputsWithoutTracking; total.OutputsWithoutTracking += one.OutputsWithoutTracking;
                }
                await _uow.SaveChangesAsync(ct);
                after = ids[ids.Count - 1];
            }
            if (total.OrdersWithoutKit > 0) total.Notes.Add($"{total.OrdersWithoutKit} دستور تولید کیت (درخواست مواد) نداشت: حواله‌های آن‌ها به دستور وصل نیست و پیوند ساخته نشد (حواله‌هایی که بدون ارجاع به کیت صادر شده‌اند قابل بازسازی نیستند).");
            if (total.OrdersReceiptNotConfirmed > 0) total.Notes.Add($"{total.OrdersReceiptNotConfirmed} دستور تولید رسید ساخت تاییدشده نداشت (پیش‌نویس یا باطل): با تایید رسید پیوند ساخته می‌شود.");
            if (total.OrdersWithoutIssuedMaterial > 0) total.Notes.Add($"{total.OrdersWithoutIssuedMaterial} دستور تولید با رسید تاییدشده هیچ حواله تاییدشده‌ای علیه کیت خود نداشت (مصرف ثبت نشده است).");
            if (total.InputsWithoutTracking > 0) total.Notes.Add($"در {total.InputsWithoutTracking} دستور، ورودی بدون بچ و سریال بود (کالای بدون ردیابی): پیوند فقط در سطح کالا ثبت شد و ردیابی معکوس از آن عبور نمی‌کند.");
            if (total.OutputsWithoutTracking > 0) total.Notes.Add($"در {total.OutputsWithoutTracking} دستور، خروجی بدون بچ و سریال بود: پیوند فقط در سطح کالا ثبت شد.");
            total.Notes.Add("سهم هر ورودی از هر سریال خروجی (وقتی دستور چند سریال می‌سازد) برابر تقسیم مساوی است؛ مصرف واقعی هر قطعه جداگانه ثبت نشده بود. ورودی مصرف‌شده در عملیات مشخص نیز ثبت نشده بود: کیت برای اولین عملیات دستور ثبت می‌شود.");
            return ResponseDtoHelper.Ok(total);
        }
    }
}
