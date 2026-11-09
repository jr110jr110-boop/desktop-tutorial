using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProjectControl;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.GoLiveDtos;

namespace Services.NasbNiro.GoLive
{
    /// <summary>
    /// Package G (ISO 9001 8.4.3): a purchase order line cites the drawing number and revision in force when the order was saved, approved and released. The citation is read from the EDMS (never typed). When the revision is superseded
    /// while the order is open, the buyer and engineering are alerted, and the line is "stale" (a P9 finding) until the line is revised (the order revision re-cites) or the buyer records the supplier's confirmation of the new revision.
    /// </summary>
    public static class PoDrawingCitation
    {
        public static readonly PurchaseOrderStatus[] OpenStatuses = { PurchaseOrderStatus.Approved, PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived };

        /// <summary>Cites (or refreshes the citation of) the line. Returns true when the line changed. Safe to call again.</summary>
        public static async Task<bool> CiteAsync(IUnitOfWork uow, PurchaseOrderLine line, IReadOnlyDictionary<string, decimal> settings, CancellationToken ct)
        {
            if (line == null || !line.ItemId.HasValue) return false;
            if (settings != null && settings.TryGetValue(GlSettingKeys.PoCiteDrawing, out decimal on) && on < 1m) return false;
            List<PartDrawing> drawings = await EdmsResolver.DrawingsForItemAsync(uow, line.ItemId.Value, null, ct);
            PartDrawing d = drawings.OrderBy(x => x.Number, StringComparer.Ordinal).FirstOrDefault();
            if (d == null) return false;
            if (line.DrawingRevisionId == d.RevisionId && line.DrawingDeliverableId == d.DeliverableId) return false;
            line.DrawingDeliverableId = d.DeliverableId; line.DrawingRevisionId = d.RevisionId; line.DrawingNumber = GlText.Cut(d.Number, 100); line.DrawingRevision = GlText.Cut(d.RevisionCode, 30); line.DrawingCitedAt = DateTimeOffset.Now;
            if (line.SupplierConfirmedRevisionId.HasValue && line.SupplierConfirmedRevisionId != d.RevisionId) { line.SupplierConfirmedRevisionId = null; line.SupplierConfirmedRevision = null; line.SupplierConfirmedAt = null; line.SupplierConfirmedByUserId = null; line.SupplierConfirmedRef = null; }
            return true;
        }

        public sealed class Stale
        {
            public PurchaseOrderLine Line;
            public string OrderNumber;
            public int OrderId;
            public int? OrderCreatorUserId;
            public int CurrentRevisionId;
            public string CurrentRevision;
        }

        /// <summary>The open lines whose cited revision is no longer the revision in force and that the supplier did not confirm on the new revision.</summary>
        public static async Task<List<Stale>> StaleAsync(IUnitOfWork uow, int? deliverableId, CancellationToken ct)
        {
            IQueryable<PurchaseOrderLine> q = uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.DrawingRevisionId != null && l.DrawingDeliverableId != null && OpenStatuses.Contains(l.PurchaseOrder.Status) && !l.PurchaseOrder.Deleted);
            if (deliverableId.HasValue) q = q.Where(l => l.DrawingDeliverableId == deliverableId.Value);
            var lines = await q.Select(l => new { Line = l, l.PurchaseOrder.Number, OrderId = l.PurchaseOrderId, l.PurchaseOrder.CreatorUserId }).Take(2000).ToListAsync(ct);
            if (lines.Count == 0) return new List<Stale>();
            List<int> docIds = lines.Select(x => x.Line.DrawingDeliverableId.Value).Distinct().ToList();
            var revs = await uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted && docIds.Contains(r.DeliverableId) && (r.Status == PcDocStatus.Released || r.Status == PcDocStatus.AsBuilt)).Select(r => new { r.Id, r.DeliverableId, r.Code, r.Sequence }).ToListAsync(ct);
            Dictionary<int, (int Id, string Code)> current = revs.GroupBy(r => r.DeliverableId).ToDictionary(g => g.Key, g => { var top = g.OrderByDescending(r => r.Sequence).ThenByDescending(r => r.Id).First(); return (top.Id, top.Code); });
            var result = new List<Stale>();
            foreach (var x in lines)
            {
                if (!current.TryGetValue(x.Line.DrawingDeliverableId.Value, out var cur)) continue;
                if (cur.Id == x.Line.DrawingRevisionId) continue;
                if (x.Line.SupplierConfirmedRevisionId == cur.Id) continue;
                result.Add(new Stale { Line = x.Line, OrderNumber = x.Number, OrderId = x.OrderId, OrderCreatorUserId = x.CreatorUserId, CurrentRevisionId = cur.Id, CurrentRevision = cur.Code });
            }
            return result;
        }

        public static string Key(int lineId) => "po-drawing:" + lineId;

        /// <summary>Opens (and closes) the alerts to the buyer and engineering. Never throws.</summary>
        public static async Task SyncAlertsAsync(IUnitOfWork uow, INotificationService notify, int? deliverableId, CancellationToken ct)
        {
            if (notify == null) return;
            try
            {
                List<Stale> stale = await StaleAsync(uow, deliverableId, ct);
                var staleIds = stale.Select(s => s.Line.Id).ToHashSet();
                foreach (Stale s in stale)
                {
                    var req = new NotifyRequest
                    {
                        SourceKey = Key(s.Line.Id), Module = "procurement", SourceType = "po-drawing", SourceId = s.OrderId,
                        Title = $"نقشه {s.Line.DrawingNumber} در سفارش {s.OrderNumber} بازنگری {s.Line.DrawingRevision} است ولی بازنگری {s.CurrentRevision} منتشر شده؛ ردیف را اصلاح یا تایید تامین‌کننده را ثبت کنید",
                        Link = "/procurement/orders/" + s.OrderId, Roles = "COMMERCIAL,ENGINEERING", Priority = Entities.Model.NasbNiro.Office.OfUrgency.Urgent, ReplaceRecipients = true,
                    };
                    if (s.OrderCreatorUserId.HasValue && s.OrderCreatorUserId.Value > 0) req.UserIds.Add(s.OrderCreatorUserId.Value);
                    await notify.NotifyAsync(req, ct);
                }
                IQueryable<PurchaseOrderLine> cited = uow.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.DrawingRevisionId != null);
                if (deliverableId.HasValue) cited = cited.Where(l => l.DrawingDeliverableId == deliverableId.Value);
                foreach (int id in await cited.Select(l => l.Id).Take(2000).ToListAsync(ct))
                    if (!staleIds.Contains(id)) await notify.CloseAsync(Key(id), "ردیف اصلاح یا تایید تامین‌کننده ثبت شد", ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { /* the inbox never blocks a flow */ }
        }
    }

    public interface IPoCitationService
    {
        Task<ResponseDto<List<GlPoLineCitationDto>>> StaleLinesAsync(CancellationToken ct);
        Task<ResponseDto<GlPoLineCitationDto>> ConfirmAsync(GlPoConfirmDto dto, CancellationToken ct);
        Task<ResponseDto<int>> SyncAsync(CancellationToken ct);
    }

    public class PoCitationService : PbServiceBase, IPoCitationService
    {
        private readonly INotificationService _notify;
        public PoCitationService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify) : base(uow, user) { _notify = notify; }

        private static GlPoLineCitationDto ToDto(PoDrawingCitation.Stale s, string itemCode) => new GlPoLineCitationDto
        {
            LineId = s.Line.Id, OrderId = s.OrderId, OrderNumber = s.OrderNumber, ItemId = s.Line.ItemId, ItemCode = itemCode, DrawingNumber = s.Line.DrawingNumber, DrawingRevision = s.Line.DrawingRevision, DrawingRevisionId = s.Line.DrawingRevisionId,
            Stale = true, CurrentRevision = s.CurrentRevision, ConfirmedRevision = s.Line.SupplierConfirmedRevision,
            Problem = $"نقشه {s.Line.DrawingNumber} پس از ثبت سفارش از بازنگری {s.Line.DrawingRevision} به {s.CurrentRevision} رسیده است.",
        };

        public async Task<ResponseDto<List<GlPoLineCitationDto>>> StaleLinesAsync(CancellationToken ct)
        {
            List<PoDrawingCitation.Stale> stale = await PoDrawingCitation.StaleAsync(Uow, null, ct);
            List<int> itemIds = stale.Where(s => s.Line.ItemId.HasValue).Select(s => s.Line.ItemId.Value).Distinct().ToList();
            Dictionary<int, string> codes = await Uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct);
            return ResponseDtoHelper.Ok(stale.Select(s => ToDto(s, s.Line.ItemId.HasValue && codes.TryGetValue(s.Line.ItemId.Value, out string c) ? c : null)).ToList());
        }

        public async Task<ResponseDto<GlPoLineCitationDto>> ConfirmAsync(GlPoConfirmDto dto, CancellationToken ct)
        {
            if (IsAi) return ResponseDtoHelper.BadRequest<GlPoLineCitationDto>(GlText.AiNever);
            if (!InRole("COMMERCIAL")) return ResponseDtoHelper.BadRequest<GlPoLineCitationDto>(GlText.NotAllowed + " (بازرگانی)");
            if (dto == null || dto.LineId <= 0) return ResponseDtoHelper.BadRequest<GlPoLineCitationDto>(GlText.InvalidData);
            string reference = PbText.Clean(dto.Reference);
            if (reference == null || reference.Length < 5) return ResponseDtoHelper.BadRequest<GlPoLineCitationDto>("مرجع تایید تامین‌کننده (نامه، ایمیل، شماره پاسخ) را بنویسید.");
            if (GlText.Over(reference, 200, "مرجع تایید") is string lr) return ResponseDtoHelper.BadRequest<GlPoLineCitationDto>(lr);
            List<PoDrawingCitation.Stale> stale = await PoDrawingCitation.StaleAsync(Uow, null, ct);
            PoDrawingCitation.Stale s = stale.FirstOrDefault(x => x.Line.Id == dto.LineId);
            if (s == null) return ResponseDtoHelper.BadRequest<GlPoLineCitationDto>("این ردیف نیاز به تایید بازنگری جدید ندارد.");
            PurchaseOrderLine line = await Uow.Set<PurchaseOrderLine>().FirstAsync(l => l.Id == dto.LineId, ct);
            line.SupplierConfirmedRevisionId = s.CurrentRevisionId; line.SupplierConfirmedRevision = GlText.Cut(s.CurrentRevision, 30); line.SupplierConfirmedAt = DateTimeOffset.Now; line.SupplierConfirmedByUserId = User.UserId; line.SupplierConfirmedRef = reference;
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return ResponseDtoHelper.BadRequest<GlPoLineCitationDto>(conflict);
            await PoDrawingCitation.SyncAlertsAsync(Uow, _notify, line.DrawingDeliverableId, ct);
            var r = ToDto(s, null); r.Stale = false; r.ConfirmedRevision = s.CurrentRevision; r.Problem = null;
            return ResponseDtoHelper.Ok(r);
        }

        public async Task<ResponseDto<int>> SyncAsync(CancellationToken ct)
        {
            await PoDrawingCitation.SyncAlertsAsync(Uow, _notify, null, ct);
            return ResponseDtoHelper.Ok((await PoDrawingCitation.StaleAsync(Uow, null, ct)).Count);
        }
    }
}
