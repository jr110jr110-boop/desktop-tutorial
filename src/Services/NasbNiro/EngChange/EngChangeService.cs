using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.EngChange;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.Production;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.ProductBank;
using ViewModels.NasbNiro.EngChangeDtos;
using ViewModels.NasbNiro.ProductBankDtos;

namespace Services.NasbNiro.EngChange
{
    /// <summary>Document number kinds of Package A2 (the band 791..792; 741..748 are Package 6).</summary>
    public static class EcNumbers
    {
        public const int Request = 791;
        public const int Order = 792;
    }

    public interface IEngChangeService
    {
        Task<ResponseDto<List<EcRequestListDto>>> GetRequestsAsync(EcRequestQuery query, CancellationToken ct);
        Task<ResponseDto<EcRequestDto>> GetRequestAsync(int id, CancellationToken ct);
        Task<ResponseDto<EcRequestDto>> SaveRequestAsync(SaveEcRequestDto dto, CancellationToken ct);
        Task<ResponseDto<EcRequestDto>> RequestActionAsync(EcRequestActionDto dto, CancellationToken ct);

        Task<ResponseDto<List<EcOrderListDto>>> GetOrdersAsync(EcOrderQuery query, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> GetOrderAsync(int id, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> SaveOrderAsync(SaveEcOrderDto dto, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> AddChangeAsync(EcOrderChangeSaveDto dto, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> RemoveChangeAsync(EcOrderChangeRemoveDto dto, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> NewRevisionAsync(EcNewRevisionDto dto, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> SaveDispositionAsync(EcDispositionSaveDto dto, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> BulkDispositionAsync(EcBulkDispositionDto dto, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> OrderActionAsync(EcOrderActionDto dto, CancellationToken ct);
        Task<ResponseDto<EcOrderDto>> TaskActionAsync(EcTaskActionDto dto, CancellationToken ct);
        Task<ResponseDto<List<EcTaskDto>>> GetMyTasksAsync(CancellationToken ct);
        Task<ResponseDto<List<EcHistoryRowDto>>> GetHistoryAsync(string partNumber, CancellationToken ct);
        Task<ResponseDto<EcGateCheckDto>> CheckAsync(EcChangeKind kind, int targetId, EcChangeAction action, CancellationToken ct);
    }

    /// <summary>
    /// Package A2: the engineering change request (ECR) and order (ECO). The impact is computed on the server and stored with the order (JSON + SHA-256); every impacted item needs a disposition before the order is approved;
    /// the order is approved by ENGINEERING and QA, two distinct persons who are neither the originator nor an editor, never an AI seat; PMO, the CEO and the affected project managers are told. The gates in <see cref="EcGate"/> use the approved orders.
    /// </summary>
    public partial class EngChangeService : IEngChangeService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IPbBomService _bom;
        private readonly IPbRoutingService _opc;

        public EngChangeService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify = null, IPbBomService bom = null, IPbRoutingService opc = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify;
            _bom = bom;
            _opc = opc;
        }

        private int Me => _user.UserId;
        private static bool IsAi => Ai.AiActing.IsActing;
        private bool InRole(string roles) => _user.IsInRole(roles);

        private static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        private const string NotAllowed = "شما مجوز این کار را ندارید.";
        private const string NotFound = "پیدا نشد.";
        private const string AiNeverApproves = "ثبت، تایید و تصمیم در تغییرات مهندسی با هوش مصنوعی انجام نمی‌شود؛ یک کاربر انسانی باید آن را انجام دهد.";
        private const string Concurrent = "همزمان کس دیگری همین سند را تغییر داد؛ صفحه را دوباره بارگذاری کنید.";

        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static void Stamp(BaseEntity e, bool isNew, int userId)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (isNew) { e.CreateDate = now; e.CreatorUserId = userId; }
            e.ModifyDate = now; e.ModifierUserId = userId;
        }

        private async Task<string> TrySaveAsync(CancellationToken ct)
        {
            try { await _uow.SaveChangesAsync(ct); return null; }
            catch (DbUpdateConcurrencyException) { return Concurrent; }
        }

        // ============================================================================================== texts

        public static string StatusText(EcrStatus s)
        {
            switch (s)
            {
                case EcrStatus.Draft: return "پیش‌نویس";
                case EcrStatus.Submitted: return "منتظر تصمیم مهندسی";
                case EcrStatus.Approved: return "تایید شده";
                case EcrStatus.Rejected: return "رد شده";
                case EcrStatus.Cancelled: return "لغو شده";
                default: return s.ToString();
            }
        }

        public static string StatusText(EcoStatus s)
        {
            switch (s)
            {
                case EcoStatus.Draft: return "پیش‌نویس";
                case EcoStatus.InApproval: return "در حال تایید";
                case EcoStatus.Approved: return "تایید شده، در حال اجرا";
                case EcoStatus.Rejected: return "رد شده";
                case EcoStatus.Closed: return "بسته شده";
                case EcoStatus.Cancelled: return "لغو شده";
                default: return s.ToString();
            }
        }

        // ============================================================================================== requests

        private async Task<EcRequest> LoadRequestAsync(int id, bool tracked, CancellationToken ct)
        {
            IQueryable<EcRequest> q = _uow.Set<EcRequest>().Where(r => r.Id == id && !r.Deleted);
            return await (tracked ? q.Include(r => r.Targets) : q.AsNoTracking().Include(r => r.Targets)).FirstOrDefaultAsync(ct);
        }

        public async Task<ResponseDto<List<EcRequestListDto>>> GetRequestsAsync(EcRequestQuery query, CancellationToken ct)
        {
            if (!InRole(EcRules.Readers)) return Bad<List<EcRequestListDto>>(NotAllowed);
            query ??= new EcRequestQuery();
            IQueryable<EcRequest> q = _uow.Set<EcRequest>().AsNoTracking().Include(r => r.Targets).Where(r => !r.Deleted);
            if (query.Status.HasValue) q = q.Where(r => r.Status == query.Status.Value);
            string text = Clean(query.Contains);
            if (text != null) q = q.Where(r => r.Number.Contains(text) || r.Title.Contains(text));
            string pn = Clean(query.PartNumber)?.ToUpperInvariant();
            if (pn != null) q = q.Where(r => r.Targets.Any(t => t.PartNumber == pn));
            List<EcRequest> rows = await q.OrderByDescending(r => r.Id).Take(500).ToListAsync(ct);
            return ResponseDtoHelper.Ok(await ListDtosAsync(rows, ct));
        }

        private async Task<List<EcRequestListDto>> ListDtosAsync(List<EcRequest> rows, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, rows.Select(r => (int?)r.RequestedByUserId), ct);
            List<int> ids = rows.Select(r => r.Id).ToList();
            var orders = ids.Count == 0 ? new List<EcOrder>() : await _uow.Set<EcOrder>().AsNoTracking().Where(o => !o.Deleted && ids.Contains(o.RequestId) && o.Status != EcoStatus.Rejected && o.Status != EcoStatus.Cancelled).ToListAsync(ct);
            return rows.Select(r =>
            {
                EcOrder o = orders.FirstOrDefault(x => x.RequestId == r.Id);
                return new EcRequestListDto
                {
                    Id = r.Id, Number = r.Number, Title = r.Title, Classification = r.Classification, Urgency = r.Urgency, Status = r.Status, StatusText = StatusText(r.Status),
                    RequestedByName = names.Name(r.RequestedByUserId), CreatedAt = r.CreateDate, Parts = string.Join("، ", r.Targets.Select(t => t.PartNumber).Where(p => p != null).Distinct()),
                    OrderId = o?.Id, OrderNumber = o?.Number,
                };
            }).ToList();
        }

        public async Task<ResponseDto<EcRequestDto>> GetRequestAsync(int id, CancellationToken ct)
        {
            if (!InRole(EcRules.Readers)) return Bad<EcRequestDto>(NotAllowed);
            EcRequest r = await LoadRequestAsync(id, false, ct);
            return r == null ? ResponseDtoHelper.NotFound<EcRequestDto>(NotFound) : ResponseDtoHelper.Ok(await RequestDtoAsync(r, ct));
        }

        private async Task<EcRequestDto> RequestDtoAsync(EcRequest r, CancellationToken ct)
        {
            EcRequestListDto head = (await ListDtosAsync(new List<EcRequest> { r }, ct))[0];
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { r.DecidedByUserId }, ct);
            bool human = !IsAi;
            bool mine = r.RequestedByUserId == Me;
            var dto = new EcRequestDto
            {
                Id = head.Id, Number = head.Number, Title = head.Title, Classification = head.Classification, Urgency = head.Urgency, Status = head.Status, StatusText = head.StatusText,
                RequestedByName = head.RequestedByName, CreatedAt = head.CreatedAt, Parts = head.Parts, OrderId = head.OrderId, OrderNumber = head.OrderNumber,
                Reason = r.Reason, Description = r.Description, RequestedByUserId = r.RequestedByUserId, SubmittedAt = r.SubmittedAt, DecidedByName = names.Name(r.DecidedByUserId), DecidedAt = r.DecidedAt,
                DecisionNote = r.DecisionNote, RowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
                Targets = r.Targets.OrderBy(t => t.Id).Select(t => new EcTargetDto
                {
                    Id = t.Id, Kind = t.Kind, PartNumber = t.PartNumber, ProductId = t.ProductId, BomHeaderId = t.BomHeaderId, OpcRoutingId = t.OpcRoutingId, DeliverableId = t.DeliverableId,
                    RoutingRevisionId = t.RoutingRevisionId, Reference = t.Reference, Note = t.Note,
                }).ToList(),
            };
            dto.CanEdit = human && r.Status == EcrStatus.Draft && mine;
            dto.CanSubmit = dto.CanEdit;
            dto.CanDecide = human && r.Status == EcrStatus.Submitted && InRole(EcRules.RequestApprovers) && !mine;
            dto.CanCancel = human && (r.Status == EcrStatus.Draft || r.Status == EcrStatus.Submitted) && (mine || InRole(EcRules.RequestApprovers)) && head.OrderId == null;
            dto.CanCreateOrder = human && r.Status == EcrStatus.Approved && head.OrderId == null && InRole(EcRules.OrderMakers);
            return dto;
        }

        /// <summary>Checks every target against the registers and fills the part number and the reference text from them.</summary>
        private async Task<string> ResolveTargetsAsync(List<EcTargetDto> targets, List<EcRequestTarget> output, int requestId, CancellationToken ct)
        {
            foreach (EcTargetDto t in targets)
            {
                var row = new EcRequestTarget { RequestId = requestId, Kind = t.Kind, Note = Clean(t.Note) };
                switch (t.Kind)
                {
                    case EcTargetKind.Part:
                    {
                        string pn = PbText.PartNumber(t.PartNumber);
                        string shape = PbCodeRules.ShapeError(pn);
                        if (shape != null) return shape;
                        row.PartNumber = pn;
                        InventoryItem item = await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => !i.Deleted && i.PartNumber == pn && i.StageCode2 == null, ct);
                        row.Reference = pn + (item == null ? "" : " " + item.Title);
                        break;
                    }
                    case EcTargetKind.Product:
                    {
                        PbProduct p = t.ProductId.HasValue ? await _uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.ProductId.Value && !x.Deleted, ct) : null;
                        if (p == null) return "محصول بانک محصول پیدا نشد.";
                        row.ProductId = p.Id; row.PartNumber = p.Code; row.Reference = p.Code + " " + p.Title;
                        break;
                    }
                    case EcTargetKind.Bom:
                    {
                        PbBomHeader h = t.BomHeaderId.HasValue ? await _uow.Set<PbBomHeader>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.BomHeaderId.Value && !x.Deleted, ct) : null;
                        if (h == null) return "BOM پیدا نشد.";
                        row.BomHeaderId = h.Id; row.PartNumber = h.AssemblyPartNumber; row.ProductId = h.ProductId; row.Reference = $"{h.Code} بازنگری {h.Revision:00}";
                        break;
                    }
                    case EcTargetKind.Opc:
                    {
                        PbOpcRouting r = t.OpcRoutingId.HasValue ? await _uow.Set<PbOpcRouting>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.OpcRoutingId.Value && !x.Deleted, ct) : null;
                        if (r == null) return "چارت OPC پیدا نشد.";
                        row.OpcRoutingId = r.Id; row.PartNumber = r.PartNumber; row.ProductId = r.ProductId; row.Reference = $"OPC {r.PartNumber} بازنگری {r.Revision:00}";
                        break;
                    }
                    case EcTargetKind.Drawing:
                    {
                        PcEngineeringDeliverable d = t.DeliverableId.HasValue ? await _uow.Set<PcEngineeringDeliverable>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.DeliverableId.Value && !x.Deleted, ct) : null;
                        if (d == null) return "سند (نقشه) پیدا نشد.";
                        row.DeliverableId = d.Id; row.Reference = $"{d.DocumentNumber} {d.Title}";
                        row.PartNumber = Clean(t.PartNumber)?.ToUpperInvariant();
                        break;
                    }
                    case EcTargetKind.Routing:
                    {
                        MfRoutingRevision r = t.RoutingRevisionId.HasValue ? await _uow.Set<MfRoutingRevision>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.RoutingRevisionId.Value && !x.Deleted, ct) : null;
                        if (r == null) return "بازنگری مسیر ساخت پیدا نشد.";
                        InventoryItem item = r.ItemId.HasValue ? await _uow.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == r.ItemId.Value, ct) : null;
                        row.RoutingRevisionId = r.Id; row.PartNumber = item?.PartNumber; row.Reference = $"مسیر ساخت {item?.Code} بازنگری {r.Revision}";
                        break;
                    }
                    default:
                        return "نوع مورد متاثر نامعتبر است.";
                }
                output.Add(row);
            }
            return null;
        }

        public async Task<ResponseDto<EcRequestDto>> SaveRequestAsync(SaveEcRequestDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<EcRequestDto>(AiNeverApproves);
            if (!InRole(EcRules.RequestRaisers)) return Bad<EcRequestDto>(NotAllowed);
            if (dto == null || Clean(dto.Title) == null) return Bad<EcRequestDto>("عنوان درخواست تغییر را بنویسید.");
            if (Clean(dto.Reason) == null || dto.Reason.Trim().Length < 10) return Bad<EcRequestDto>("دلیل تغییر را بنویسید (دست‌کم ۱۰ نویسه).");
            if (!Enum.IsDefined(typeof(EcClass), dto.Classification) || !Enum.IsDefined(typeof(EcUrgency), dto.Urgency)) return Bad<EcRequestDto>("رده یا فوریت نامعتبر است.");
            if (dto.Targets == null || dto.Targets.Count == 0) return Bad<EcRequestDto>("دست‌کم یک محصول، قطعه یا بازنگری متاثر را بنویسید.");
            EcRequest r;
            if (dto.Id.HasValue)
            {
                r = await LoadRequestAsync(dto.Id.Value, true, ct);
                if (r == null) return ResponseDtoHelper.NotFound<EcRequestDto>(NotFound);
                if (r.Status != EcrStatus.Draft) return Bad<EcRequestDto>("فقط پیش‌نویس درخواست ویرایش می‌شود.");
                if (r.RequestedByUserId != Me) return Bad<EcRequestDto>("پیش‌نویس را فقط درخواست‌کننده ویرایش می‌کند.");
            }
            else
            {
                r = new EcRequest { Status = EcrStatus.Draft, RequestedByUserId = Me, Number = await InventoryNumbers.NextNumberAsync(_uow, EcNumbers.Request, "EC-ECR", DateTime.Today, ct) };
                Stamp(r, true, Me);
                _uow.Set<EcRequest>().Add(r);
            }
            var targets = new List<EcRequestTarget>();
            string problem = await ResolveTargetsAsync(dto.Targets, targets, r.Id, ct);
            if (problem != null) return Bad<EcRequestDto>(problem);
            r.Title = dto.Title.Trim(); r.Reason = dto.Reason.Trim(); r.Description = Clean(dto.Description); r.Classification = dto.Classification; r.Urgency = dto.Urgency;
            foreach (EcRequestTarget old in r.Targets.ToList()) r.Targets.Remove(old);   // replaced through the tracked collection (the orphans are deleted on save)
            foreach (EcRequestTarget t in targets) r.Targets.Add(t);
            Stamp(r, false, Me);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcRequestDto>(conflict);
            return ResponseDtoHelper.Ok(await RequestDtoAsync(await LoadRequestAsync(r.Id, false, ct), ct));
        }

        public async Task<ResponseDto<EcRequestDto>> RequestActionAsync(EcRequestActionDto dto, CancellationToken ct)
        {
            if (IsAi) return Bad<EcRequestDto>(AiNeverApproves);
            EcRequest r = dto == null ? null : await LoadRequestAsync(dto.Id, true, ct);
            if (r == null) return ResponseDtoHelper.NotFound<EcRequestDto>(NotFound);
            string action = (dto.Action ?? "").Trim().ToLowerInvariant();
            string note = Clean(dto.Note);
            DateTimeOffset now = DateTimeOffset.Now;
            switch (action)
            {
                case "submit":
                    if (r.Status != EcrStatus.Draft) return Bad<EcRequestDto>("فقط پیش‌نویس برای تصمیم ارسال می‌شود.");
                    if (r.RequestedByUserId != Me || !InRole(EcRules.RequestRaisers)) return Bad<EcRequestDto>("پیش‌نویس را فقط درخواست‌کننده ارسال می‌کند.");
                    if (r.Targets.Count == 0) return Bad<EcRequestDto>("درخواست مورد متاثر ندارد.");
                    r.Status = EcrStatus.Submitted; r.SubmittedAt = now;
                    break;
                case "approve":
                case "reject":
                    if (r.Status != EcrStatus.Submitted) return Bad<EcRequestDto>("فقط درخواست منتظر تصمیم تایید یا رد می‌شود.");
                    if (!InRole(EcRules.RequestApprovers)) return Bad<EcRequestDto>(NotAllowed);
                    if (r.RequestedByUserId == Me || r.CreatorUserId == Me) return Bad<EcRequestDto>("درخواست‌کننده درخواست خودش را تایید یا رد نمی‌کند.");
                    if (action == "reject" && note == null) return Bad<EcRequestDto>("دلیل رد را بنویسید.");
                    r.Status = action == "approve" ? EcrStatus.Approved : EcrStatus.Rejected; r.DecidedByUserId = Me; r.DecidedAt = now; r.DecisionNote = note;
                    break;
                case "cancel":
                    if (r.Status != EcrStatus.Draft && r.Status != EcrStatus.Submitted) return Bad<EcRequestDto>("فقط درخواست پیش‌نویس یا منتظر تصمیم لغو می‌شود.");
                    if (r.RequestedByUserId != Me && !InRole(EcRules.RequestApprovers)) return Bad<EcRequestDto>(NotAllowed);
                    if (note == null) return Bad<EcRequestDto>("دلیل لغو را بنویسید.");
                    r.Status = EcrStatus.Cancelled; r.DecidedByUserId = Me; r.DecidedAt = now; r.DecisionNote = note;
                    break;
                default:
                    return Bad<EcRequestDto>("عمل نامعتبر است.");
            }
            Stamp(r, false, Me);
            string conflict = await TrySaveAsync(ct);
            if (conflict != null) return Bad<EcRequestDto>(conflict);
            if (action == "submit")
                await Tell($"ec:ecr:{r.Id}", $"درخواست تغییر مهندسی {r.Number} منتظر تصمیم مهندسی است", "EcRequest", r.Id, "/eng-change/requests/" + r.Id, EcRules.RequestApprovers, null, true, ct);
            else await CloseTell($"ec:ecr:{r.Id}", "تصمیم گرفته شد", ct);
            return ResponseDtoHelper.Ok(await RequestDtoAsync(await LoadRequestAsync(r.Id, false, ct), ct));
        }

        // ============================================================================================== notifications

        private async Task Tell(string key, string title, string sourceType, int id, string link, string roles, IEnumerable<int> users, bool approval, CancellationToken ct)
        {
            if (_notify == null) return;
            await _notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = key, Module = "engchange", SourceType = sourceType, SourceId = id, Title = title, Link = link, Roles = roles,
                UserIds = (users ?? new int[0]).Where(u => u > 0 && u != Me).Distinct().ToList(), Kind = approval ? OfInboxKind.Approval : OfInboxKind.Alert, Priority = OfUrgency.Normal,
            }, ct);
        }

        private async Task CloseTell(string key, string reason, CancellationToken ct)
        {
            if (_notify != null) await _notify.CloseAsync(key, reason, ct);
        }
    }
}
