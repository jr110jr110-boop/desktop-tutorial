using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Attachments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Procurement;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using ViewModels.NasbNiro.AttachmentDtos;
using ViewModels.NasbNiro.TraceDtos;

namespace Services.NasbNiro.Trace
{
    public sealed partial class TraceCenterService
    {
        // ------------------------------------------------------------------------------------------ read model pieces (all in memory once loaded)

        private sealed class UnitInfo
        {
            public string Key;
            public bool IsLot;
            public bool Untracked;
            public int Id;
            public int ItemId;
            public string Number;
            public string Heat;
            public string Cert;
            public Guid? CertDocument;
            public int? ProviderId;
            public string Status;
            public int? WorkOrderId;
            public int? LotId;
            public int? ParentLotId;
            public int? SourceSerialId;
            public int? SourceLotId;
            public DateTime? Date;
        }

        private sealed class InputEdge
        {
            public string ToKey;
            public decimal Quantity;
            public string Via;
            public int? OrderId;
            public int? IssueDocumentId;
            public bool Shared;
        }

        private sealed class OrderInfo
        {
            public int Id;
            public string Number;
            public int WorkOrderId;
            public int? ItemId;
            public MfOrderStatus Status;
            public decimal Quantity;
            public string Unit;
            public int? ReceiptId;
        }

        private sealed class OpInfo
        {
            public int Id;
            public int OrderId;
            public int Sequence;
            public string Title;
            public MfOperationStatus Status;
            public decimal Good;
            public decimal Scrap;
            public decimal Rework;
            public bool QcAccepted;
            public string QcNumber;
            public QcResult? QcResult;
            public int? NcrId;
            public string NcrNumber;
            public bool IsOutside;
            public bool Concession;
        }

        private sealed class InspInfo
        {
            public int Id;
            public string Number;
            public InspectionStatus Status;
            public decimal Accepted;
            public decimal Rejected;
            public string ReportNumber;
            public int ReceiptDocumentId;
            public int? LotId;
            public int? NcrId;
            public DateTimeOffset? InspectedAt;
        }

        private sealed class NcrInfo
        {
            public int Id;
            public string Number;
            public NcrStatus Status;
            public NcrSeverity Severity;
            public int ItemId;
            public int? LotId;
            public DateTime Date;
            public string Description;
            public string SerialNumbers;
        }

        private sealed class PartOpInfo
        {
            public int Id;
            public string Title;
            public PartOperationStatus Status;
            public QcStatus Qc;
            public DateTimeOffset? QcAt;
            public int? SerialId;
            public int? LotId;
            public int Sequence;
            public string Note;
        }

        private sealed class StructLine
        {
            public string Assembly;
            public string PartNumber;
            public string Name;
            public decimal Qty;
            public string Unit;
            public string Material;
            public PbMakeOrBuy Mob;
            public int? ItemId;
            public int? ItemNumber;
            public int SortOrder;
        }

        private sealed class StepInfo
        {
            public string PartNumber;
            public int Seq;
            public string StageCode;
            public string Title;
            public string Kind;
            public bool IsQc;
            public string Evidence;
            public bool Outside;
            public int? StageItemId;
            public decimal? Hours;
            public string RoutingTitle;
            public int Revision;
        }

        private sealed class StepMap : Dictionary<string, List<StepInfo>>
        {
            public void AddStep(StepInfo s)
            {
                if (!TryGetValue(s.PartNumber, out List<StepInfo> list)) this[s.PartNumber] = list = new List<StepInfo>();
                list.Add(s);
            }
        }

        private sealed class TreeCtx
        {
            public readonly Dictionary<string, UnitInfo> Units = new Dictionary<string, UnitInfo>();
            public readonly Dictionary<string, List<InputEdge>> Inputs = new Dictionary<string, List<InputEdge>>();
            public readonly Dictionary<int, (string Code, string Title, string PartNumber, string Stage)> Items = new Dictionary<int, (string, string, string, string)>();
            public readonly Dictionary<int, OrderInfo> Orders = new Dictionary<int, OrderInfo>();
            public readonly Dictionary<int, List<OpInfo>> OpsByOrder = new Dictionary<int, List<OpInfo>>();
            public List<ReceiptRow> Receipts = new List<ReceiptRow>();
            public List<InspInfo> Inspections = new List<InspInfo>();
            public List<NcrInfo> Ncrs = new List<NcrInfo>();
            public List<PartOpInfo> PartOps = new List<PartOpInfo>();
            public readonly Dictionary<string, List<TraceFileDto>> Files = new Dictionary<string, List<TraceFileDto>>();
            public readonly Dictionary<int, List<TraceFileDto>> RequestFiles = new Dictionary<int, List<TraceFileDto>>();
            public readonly Dictionary<int, List<TraceFileDto>> RequestLineFiles = new Dictionary<int, List<TraceFileDto>>();
            public readonly Dictionary<int, string> Providers = new Dictionary<int, string>();
            public readonly Dictionary<string, List<ReverseDeliveryDto>> Deliveries = new Dictionary<string, List<ReverseDeliveryDto>>();
            public readonly Dictionary<string, string> Stages = new Dictionary<string, string>();
            public readonly StepMap StepsByPart = new StepMap();
            public List<int> SeedLots = new List<int>();
            public List<int> SeedSerials = new List<int>();
            public int NodeCount;
            public bool Truncated;
            public int MaxNodes = 6000;
            public int MaterialDepth = 4;
        }

        private static string FileKey(AttachmentOwnerType t, int id) => (int)t + ":" + id;

        // ------------------------------------------------------------------------------------------ entry

        public async Task<ResponseDto<TraceTreeDto>> TreeAsync(TraceQuery query, CancellationToken ct)
        {
            string text = Clean(query?.Q);
            if (text == null && query?.SerialUnitId == null && query?.LotId == null) return Bad<TraceTreeDto>("کد محصول، شماره قطعه، سریال یا شماره بچ/ذوب را وارد کنید.");
            int depth = Math.Max(1, Math.Min(query.Depth ?? DefaultTreeDepth, MaxTreeDepth));
            var dto = new TraceTreeDto { Query = text, MaxDepth = depth, MaxParts = MaxParts, PricesMasked = !SeesPrices };
            var c = new TreeCtx();

            Resolution r = await ResolveAsync(query, text, dto, ct);
            if (r.Mode == null)
            {
                dto.Mode = "choices";
                if (dto.Choices.Count == 0) return ResponseDtoHelper.NotFound<TraceTreeDto>("محصول، قطعه، سریال یا بچی با این مشخصه پیدا نشد.");
                return ResponseDtoHelper.Ok(dto);
            }
            dto.Mode = r.Mode;

            switch (r.Mode)
            {
                case "product": await BuildProductAsync(c, dto, r, query, depth, ct); break;
                case "part": await BuildPartAsync(c, dto, r, ct); break;
                default: await BuildUnitRootAsync(c, dto, r, ct); break;
            }
            if (dto.Mode == "choices") return ResponseDtoHelper.Ok(dto);
            dto.NodeCount = c.NodeCount;
            dto.Truncated = dto.Truncated || c.Truncated;
            if (c.Truncated) dto.Notes.Add($"تعداد گره‌ها به سقف {c.MaxNodes} رسید؛ بخشی از شاخه‌ها نمایش داده نشد.");
            if (dto.PricesMasked) dto.Notes.Add("قیمت و مبلغ برای نقش شما نمایش داده نمی‌شود.");
            return ResponseDtoHelper.Ok(dto);
        }

        // ------------------------------------------------------------------------------------------ resolve the text

        private sealed class Resolution
        {
            public string Mode;
            public PbProduct Product;
            public string PartNumber;
            public int? SerialId;
            public int? LotId;
        }

        private async Task<Resolution> ResolveAsync(TraceQuery query, string text, TraceTreeDto dto, CancellationToken ct)
        {
            var r = new Resolution();
            string kind = Clean(query.Kind)?.ToLowerInvariant();
            if (query.SerialUnitId.HasValue) { r.Mode = "serial"; r.SerialId = query.SerialUnitId; return r; }
            if (query.LotId.HasValue) { r.Mode = "lot"; r.LotId = query.LotId; return r; }
            if (text == null) return r;

            (string labelKind, string labelItem, string labelValue) = ParseLabel(text);
            if (labelKind == "S")
            {
                int? id = await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.Item.Code == labelItem && u.SerialNumber == labelValue).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
                if (id.HasValue) { r.Mode = "serial"; r.SerialId = id; return r; }
            }
            if (labelKind == "L")
            {
                int? id = await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && l.Item.Code == labelItem && l.LotNumber == labelValue).Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
                if (id.HasValue) { r.Mode = "lot"; r.LotId = id; return r; }
            }

            if (kind == null || kind == "product")
            {
                PbProduct product = await _uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.Code == text).OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
                if (product != null) { r.Mode = "product"; r.Product = product; return r; }
            }
            if (kind == null || kind == "serial")
            {
                var serials = await _uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.SerialNumber == text).Select(u => new { u.Id, ItemCode = u.Item.Code, ItemTitle = u.Item.Title }).Take(20).ToListAsync(ct);
                if (serials.Count == 1) { r.Mode = "serial"; r.SerialId = serials[0].Id; return r; }
                if (serials.Count > 1)
                {
                    foreach (var s in serials) dto.Choices.Add(new TraceChoiceDto { Kind = "serial", Title = "سریال " + text, Description = s.ItemCode + " " + s.ItemTitle, Q = text, SerialUnitId = s.Id });
                    return r;
                }
            }
            if (kind == null || kind == "lot")
            {
                var lots = await _uow.Set<InventoryLot>().AsNoTracking().Where(l => !l.Deleted && (l.LotNumber == text || l.HeatNumber == text || l.CertificateNumber == text))
                    .Select(l => new { l.Id, l.LotNumber, l.HeatNumber, ItemCode = l.Item.Code, ItemTitle = l.Item.Title }).Take(20).ToListAsync(ct);
                if (lots.Count == 1) { r.Mode = "lot"; r.LotId = lots[0].Id; return r; }
                if (lots.Count > 1)
                {
                    foreach (var l in lots) dto.Choices.Add(new TraceChoiceDto { Kind = "lot", Title = "بچ " + l.LotNumber, Description = $"{l.ItemCode} {l.ItemTitle}" + (l.HeatNumber != null ? " — ذوب " + l.HeatNumber : ""), Q = text, LotId = l.Id });
                    return r;
                }
            }
            if (kind == null || kind == "part")
            {
                string pn = StripStage(text);
                bool known = await _uow.Set<InventoryItem>().AsNoTracking().AnyAsync(i => !i.Deleted && (i.PartNumber == pn || i.PartNumber == text || i.Code == text || i.Code == pn), ct)
                    || await _uow.Set<PbBomLine>().AsNoTracking().AnyAsync(l => !l.Deleted && !l.IsHeading && (l.PartNumber == pn || l.PartNumber == text), ct);
                if (known) { r.Mode = "part"; r.PartNumber = await ExactPartNumberAsync(text, pn, ct); return r; }
            }

            // a word of a title: products and items, as choices
            foreach (PbProduct p in await _uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && (p.Title.Contains(text) || p.Code.Contains(text))).OrderBy(p => p.Code).Take(10).ToListAsync(ct))
                dto.Choices.Add(new TraceChoiceDto { Kind = "product", Title = p.Code, Description = p.Title, Q = p.Code });
            foreach (var i in await _uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber != null && i.StageCode2 == null && (i.Title.Contains(text) || i.PartNumber.Contains(text))).OrderBy(i => i.PartNumber).Take(15)
                .Select(i => new { i.PartNumber, i.Title }).ToListAsync(ct))
                dto.Choices.Add(new TraceChoiceDto { Kind = "part", Title = i.PartNumber, Description = i.Title, Q = i.PartNumber });
            return r;
        }

        private async Task<string> ExactPartNumberAsync(string text, string stripped, CancellationToken ct)
        {
            string pn = await _uow.Set<InventoryItem>().AsNoTracking().Where(i => !i.Deleted && i.PartNumber != null && (i.PartNumber == text || i.Code == text)).Select(i => i.PartNumber).FirstOrDefaultAsync(ct);
            if (pn != null) return pn;
            pn = await _uow.Set<PbBomLine>().AsNoTracking().Where(l => !l.Deleted && !l.IsHeading && l.PartNumber == text).Select(l => l.PartNumber).FirstOrDefaultAsync(ct);
            return pn ?? stripped;
        }
    }
}
