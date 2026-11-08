using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.OpsData;
using Entities.Model.NasbNiro.Office;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Office;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.OpsDataDtos;

namespace Services.NasbNiro.OpsData
{
    public sealed class ReadinessGateResult
    {
        /// <summary>The Persian refusal; null when the baseline may be created.</summary>
        public string Refusal { get; set; }
        public int? ExceptionId { get; set; }
    }

    public interface IOpsReadinessService
    {
        Task<ResponseDto<ReadinessDto>> GetAsync(int? workOrderId, int? productId, CancellationToken ct);
        Task<ResponseDto<ReadinessSummaryDto>> SummaryAsync(CancellationToken ct);
        Task<ResponseDto<List<ReadinessObligationDto>>> ObligationsAsync(int? workOrderId, bool openOnly, CancellationToken ct);

        /// <summary>The gate of the baseline instantiation: hard items missing refuse it unless the PMO and the CEO approved an exception for exactly these items.</summary>
        Task<ReadinessGateResult> GateForBaselineAsync(int workOrderId, int productId, CancellationToken ct);

        /// <summary>After the baseline exists: the missing items stay open as obligations with an owner and a due date, and the owners are told.</summary>
        Task AfterBaselineAsync(int workOrderId, int productId, CancellationToken ct);

        /// <summary>The daily run (OPS-DATA seat): recompute every running project, open obligations for new gaps, close the completed ones, remind and escalate.</summary>
        Task<string> DailyAsync(DateTime today, CancellationToken ct);

        Task<ResponseDto<ReadinessExceptionDto>> RequestExceptionAsync(RequestReadinessExceptionDto dto, CancellationToken ct);
        Task<ResponseDto<ReadinessExceptionDto>> DecideExceptionAsync(DecideReadinessExceptionDto dto, CancellationToken ct);
        Task<ResponseDto<List<ReadinessExceptionDto>>> ExceptionsAsync(int? workOrderId, CancellationToken ct);
    }

    public class OpsReadinessService : IOpsReadinessService
    {
        private readonly IUnitOfWork _uow;
        private readonly IInventoryUserContext _user;
        private readonly INotificationService _notify;
        private readonly IHttpContextAccessor _http;

        public const string Readers = "PMO,CEO,ENGINEERING,ENGINEER,DCC,PLANNING,QC,QA,PROJECTMANAGER,PRODUCTIONMANAGER,AUDITOR";
        public const string Requesters = "PMO,ENGINEERING,ENGINEER,PLANNING,PROJECTMANAGER,CONSTRUCTION,PRODUCTIONMANAGER";

        public OpsReadinessService(IUnitOfWork uow, IInventoryUserContext user, INotificationService notify, IHttpContextAccessor http = null)
        {
            _uow = uow ?? throw new ArgumentNullException(nameof(uow));
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _notify = notify ?? throw new ArgumentNullException(nameof(notify));
            _http = http;
        }

        private static ResponseDto<T> Bad<T>(string m) => ResponseDtoHelper.BadRequest<T>(m);
        private int Me => _user.UserId;
        private bool IsSeat => AiActing.IsActing || AiSeatNames.IsSeatUser(_http?.HttpContext?.User?.Identity?.Name);
        private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

        // ================================================================================================ facts

        /// <summary>Data a batch (the daily run, the summary) loads once instead of once per project.</summary>
        private sealed class BatchCache
        {
            public List<ItemUnitConversion> Conversions;
            public IReadOnlyDictionary<string, decimal> Settings;
        }

        private async Task<ReadinessFacts> LoadFactsAsync(int productId, int? workOrderId, BatchCache cache, CancellationToken ct)
        {
            var f = new ReadinessFacts { ProductId = productId, WorkOrderId = workOrderId, ProductLink = "/product-bank/products/" + N(productId) };
            PbBomHeader head = await _uow.Set<PbBomHeader>().AsNoTracking().Where(h => !h.Deleted && h.ProductId == productId && h.Status == PbBomStatus.Released).OrderByDescending(h => h.Revision).FirstOrDefaultAsync(ct);
            f.BomReleased = head != null;
            f.WbsApproved = await _uow.Set<PbWbsTemplate>().AnyAsync(t => !t.Deleted && t.ProductId == productId && t.IsApproved, ct);

            // the lines of the released BOM at every level (alternatives are options, not requirements)
            var lines = new List<PbBomLine>();
            if (head != null)
            {
                // level by level: one query per level of the BOM tree, not one per child BOM
                var loaded = new HashSet<int> { head.Id };
                var level = new List<int> { head.Id };
                while (level.Count > 0 && loaded.Count < 200)
                {
                    List<int> ids = level;
                    List<PbBomLine> part = await _uow.Set<PbBomLine>().AsNoTracking().Where(l => ids.Contains(l.BomHeaderId) && !l.Deleted && !l.IsHeading && l.AlternativeOfLineId == null).ToListAsync(ct);
                    lines.AddRange(part);
                    level = part.Where(l => l.ChildBomId.HasValue).Select(l => l.ChildBomId.Value).Distinct().Where(i => loaded.Add(i)).ToList();
                }
            }
            List<int> itemIds = lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value).Distinct().ToList();
            Dictionary<int, InventoryItem> items = itemIds.Count == 0 ? new Dictionary<int, InventoryItem>() : await _uow.Set<InventoryItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            List<ItemUnitConversion> conversions = cache?.Conversions ?? await _uow.Set<ItemUnitConversion>().AsNoTracking().Where(c => !c.Deleted && c.IsActive).ToListAsync(ct);
            if (cache != null) cache.Conversions = conversions;

            // drawings: a Released revision of a deliverable linked to the part number
            List<string> partNumbers = lines.Where(l => !string.IsNullOrWhiteSpace(l.PartNumber)).Select(l => l.PartNumber).Distinct().ToList();
            var linked = partNumbers.Count == 0 ? new List<(string Part, int Deliverable)>()
                : (await _uow.Set<PcDocumentLink>().AsNoTracking().Where(k => !k.Deleted && k.UnlinkReason == null && k.DeliverableId != null && k.PartNumber != null && partNumbers.Contains(k.PartNumber)
                        && (k.Kind == PcDocLinkKind.PartNumber || k.Kind == PcDocLinkKind.BomLine)).Select(k => new { k.PartNumber, D = k.DeliverableId.Value }).ToListAsync(ct)).Select(k => (Part: k.PartNumber, Deliverable: k.D)).ToList();
            List<int> dIds = linked.Select(k => k.Deliverable).Distinct().ToList();
            HashSet<int> released = dIds.Count == 0 ? new HashSet<int>()
                : (await _uow.Set<PcDocumentRevision>().AsNoTracking().Where(r => !r.Deleted && dIds.Contains(r.DeliverableId) && r.Status == PcDocStatus.Released).Select(r => r.DeliverableId).Distinct().ToListAsync(ct)).ToHashSet();
            HashSet<string> withDrawing = linked.Where(k => released.Contains(k.Deliverable)).Select(k => k.Part).ToHashSet();

            foreach (PbBomLine l in lines)
            {
                items.TryGetValue(l.ItemId ?? 0, out InventoryItem item);
                string unit = (l.Unit ?? string.Empty).Trim();
                string stock = (item?.Unit ?? string.Empty).Trim();
                bool differs = item != null && unit.Length > 0 && stock.Length > 0 && !string.Equals(unit, stock, StringComparison.OrdinalIgnoreCase);
                bool conv = !differs || conversions.Any(c => (c.ItemId == null || c.ItemId == item.Id) &&
                    ((string.Equals(c.FromUnit, unit, StringComparison.OrdinalIgnoreCase) && string.Equals(c.ToUnit, stock, StringComparison.OrdinalIgnoreCase)) ||
                     (string.Equals(c.FromUnit, stock, StringComparison.OrdinalIgnoreCase) && string.Equals(c.ToUnit, unit, StringComparison.OrdinalIgnoreCase))));
                bool metal = item != null && !string.IsNullOrWhiteSpace(item.Material ?? l.Material);
                f.Parts.Add(new ReadinessPart
                {
                    PartNumber = l.PartNumber, Name = l.PartName, MakeOrBuy = l.MakeOrBuy, HasItem = item != null, HasUnit = unit.Length > 0 || stock.Length > 0, ConversionMissing = !conv,
                    HasSpec = !string.IsNullOrWhiteSpace(l.Material) || !string.IsNullOrWhiteSpace(l.Standard) || (item != null && (!string.IsNullOrWhiteSpace(item.Material) || !string.IsNullOrWhiteSpace(item.Standard) || !string.IsNullOrWhiteSpace(item.Specification))),
                    IsMetal = metal, MetalWithoutWeight = metal && !(item.Weight > 0), LeadTimeMissing = item == null || item.LeadTimeDays == null,
                    HasReleasedDrawing = l.PartNumber != null && withDrawing.Contains(l.PartNumber), Link = f.ProductLink,
                });
            }

            // routings: the newest approved one of each part
            List<PbOpcRouting> routings = await _uow.Set<PbOpcRouting>().AsNoTracking().Include(r => r.Steps).Where(r => !r.Deleted && r.ProductId == productId && r.Status == PbRoutingStatus.Approved).ToListAsync(ct);
            foreach (var g in routings.GroupBy(r => r.PartNumber))
            {
                PbOpcRouting r = g.OrderByDescending(x => x.Revision).First();
                var fact = new ReadinessRoutingFact { PartNumber = r.PartNumber, Approved = true };
                foreach (PbOpcStep st in r.Steps.Where(x => !x.Deleted).OrderBy(x => x.Seq))
                {
                    string label = string.IsNullOrWhiteSpace(st.Title) ? "مرحله " + N(st.Seq) : st.Title;
                    if (st.Kind == PbStageKind.Qc || st.IsQcPoint) fact.QcPoints++;
                    if (st.Kind == PbStageKind.Operation && !st.IsOutside)
                    {
                        fact.OperationSteps++;
                        if (!st.WorkCenterId.HasValue) fact.StepsWithoutWorkCentre.Add(label);
                        if (!(st.StandardHours > 0)) fact.StepsWithoutHours.Add(label);
                    }
                    if (string.IsNullOrWhiteSpace(st.StageCode2)) fact.StepsWithoutStage.Add(label);
                }
                f.Routings[r.PartNumber ?? string.Empty] = fact;
            }

            if (workOrderId.HasValue)
            {
                int? contractId = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == workOrderId.Value).Select(w => (int?)w.ContractId).FirstOrDefaultAsync(ct);
                if (contractId.HasValue)
                {
                    f.MdrCount = await _uow.Set<PcEngineeringDeliverable>().CountAsync(d => !d.Deleted && d.ContractId == contractId.Value, ct);
                    f.ScheduleBaselineApproved = await _uow.Set<PcBaseline>().AnyAsync(b => !b.Deleted && b.ContractId == contractId.Value && b.Status == PcBaselineStatus.Approved, ct);
                }
                Itp itp = await _uow.Set<Itp>().AsNoTracking().Include(i => i.Activities).ThenInclude(a => a.Parameters).Where(i => !i.Deleted && i.WorkOrderId == workOrderId.Value).OrderByDescending(i => i.Revision).FirstOrDefaultAsync(ct);
                if (itp != null)
                {
                    f.ItpExists = true;
                    f.ItpClientApproved = itp.Status == ItpStatus.ClientApproved || itp.Status == ItpStatus.InUse || itp.Status == ItpStatus.Closed || itp.ClientApprovalDate.HasValue;
                    f.ItpActivities = itp.Activities?.Count ?? 0;
                    f.ItpActivitiesWithCharacteristics = itp.Activities?.Count(a => a.Parameters != null && a.Parameters.Count > 0) ?? 0;
                }
            }
            return f;
        }

        private async Task<ReadinessResult> ComputeAsync(int productId, int? workOrderId, CancellationToken ct, BatchCache cache = null)
        {
            ReadinessFacts facts = await LoadFactsAsync(productId, workOrderId, cache, ct);
            IReadOnlyDictionary<string, decimal> settings = cache?.Settings ?? await QualitySettingKeys.LoadAsync(_uow, ct);
            if (cache != null) cache.Settings = settings;
            return OpsReadiness.Evaluate(facts, settings);
        }

        // ================================================================================================ read

        public async Task<ResponseDto<ReadinessDto>> GetAsync(int? workOrderId, int? productId, CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(Readers)) return Bad<ReadinessDto>("شما مجوز دیدن این گزارش را ندارید.");
            string woCode = null;
            if (workOrderId.HasValue)
            {
                var wo = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == workOrderId.Value && !w.Deleted).Select(w => new { w.Code }).FirstOrDefaultAsync(ct);
                if (wo == null) return ResponseDtoHelper.NotFound<ReadinessDto>("دستور کار پیدا نشد.");
                woCode = wo.Code;
                if (!productId.HasValue)
                {
                    int? pid = await _uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == workOrderId.Value).Select(b => (int?)b.ProductId).FirstOrDefaultAsync(ct);
                    if (!pid.HasValue) return Bad<ReadinessDto>("برای این دستور کار محصول مشخص نشده است؛ محصول را هم بفرستید.");
                    productId = pid;
                }
            }
            if (!productId.HasValue) return Bad<ReadinessDto>("محصول یا دستور کار را مشخص کنید.");
            PbProduct product = await _uow.Set<PbProduct>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId.Value && !p.Deleted, ct);
            if (product == null) return ResponseDtoHelper.NotFound<ReadinessDto>("محصول پیدا نشد.");

            ReadinessResult r = await ComputeAsync(product.Id, workOrderId, ct);
            var dto = new ReadinessDto { WorkOrderId = workOrderId, WorkOrderCode = woCode, ProductId = product.Id, ProductCode = product.Code, Percent = r.Percent, HardMissing = r.HardMissing, SoftMissing = r.SoftMissing, Ready = r.HardMissing == 0, Items = r.Items };
            if (workOrderId.HasValue)
            {
                var obligations = await _uow.Set<OpsReadinessObligation>().AsNoTracking().Where(o => !o.Deleted && o.WorkOrderId == workOrderId.Value && o.Status == OpsObligationStatus.Open).ToListAsync(ct);
                foreach (ReadinessItemDto item in dto.Items)
                {
                    OpsReadinessObligation ob = obligations.FirstOrDefault(o => o.ItemCode == item.Code);
                    if (ob != null && item.Status != OpsReadiness.Ok) { item.DueDate = ob.DueDate; item.DueDateText = InventoryText.PersianDate(ob.DueDate); item.Overdue = ob.DueDate.Date < DateTime.Today; }
                }
                OpsReadinessException approved = await FindApprovedAsync(workOrderId.Value, product.Id, ct);
                dto.HasApprovedException = approved != null;
                dto.PendingExceptionId = await _uow.Set<OpsReadinessException>().AsNoTracking().Where(e => !e.Deleted && e.WorkOrderId == workOrderId.Value && e.Status == OpsReadinessExceptionStatus.Pending).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
            }
            if (r.HardGaps.Count > 0) dto.Message = OpsReadiness.Message(r.HardGaps, "اطلاعات مهندسی لازم برای شروع پروژه کامل نیست:");
            return ResponseDtoHelper.Ok(dto);
        }

        private async Task<OpsReadinessException> FindApprovedAsync(int workOrderId, int productId, CancellationToken ct) =>
            await _uow.Set<OpsReadinessException>().Where(e => !e.Deleted && e.WorkOrderId == workOrderId && e.ProductId == productId && e.Status == OpsReadinessExceptionStatus.Approved).OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);

        // ================================================================================================ gate

        public async Task<ReadinessGateResult> GateForBaselineAsync(int workOrderId, int productId, CancellationToken ct)
        {
            ReadinessResult r = await ComputeAsync(productId, workOrderId, ct);
            List<ReadinessItemDto> gaps = r.HardGaps;
            if (gaps.Count == 0) return new ReadinessGateResult();
            OpsReadinessException ex = await FindApprovedAsync(workOrderId, productId, ct);
            if (ex != null)
            {
                var covered = (ex.ItemCodes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                // the exception is spent only when the baseline has really been made (AfterBaselineAsync); a failure after the gate must not use it up
                if (gaps.All(g => covered.Contains(g.Code))) return new ReadinessGateResult { ExceptionId = ex.Id };
            }
            string text = OpsReadiness.Message(gaps, "پایه‌ریزی پروژه از بانک محصول ممکن نیست؛ اطلاعات مهندسیِ زیر کامل نیست و باید توسط مسئول هر مورد تکمیل شود:")
                + "\nاگر شروع بدون این اطلاعات ضروری است، «درخواست استثنا» با توجیه ثبت کنید؛ استثنا باید هم توسط مدیر PMO و هم توسط مدیرعامل (دو نفر غیر از درخواست‌دهنده) تأیید شود و موارد ناقص به‌عنوان تعهد با مهلت پیگیری می‌شوند.";
            return new ReadinessGateResult { Refusal = text };
        }

        public async Task AfterBaselineAsync(int workOrderId, int productId, CancellationToken ct)
        {
            ReadinessResult r = await ComputeAsync(productId, workOrderId, ct);
            OpsReadinessException spent = await FindApprovedAsync(workOrderId, productId, ct);
            if (spent != null && r.HardGaps.Count > 0)
            {
                spent.Status = OpsReadinessExceptionStatus.Used; spent.UsedAt = DateTimeOffset.Now; spent.ModifyDate = DateTimeOffset.Now;
                await _uow.SaveChangesAsync(ct);
            }
            int? exceptionId = await _uow.Set<OpsReadinessException>().AsNoTracking().Where(e => !e.Deleted && e.WorkOrderId == workOrderId && e.Status == OpsReadinessExceptionStatus.Used).OrderByDescending(e => e.Id).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
            await SyncObligationsAsync(workOrderId, productId, r, exceptionId, DateTime.Today, ct);
            string code = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == workOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            await NotifyOwnersAsync(workOrderId, code, r.Items.Where(i => i.Status != OpsReadiness.Ok).ToList(), "شروع پروژه", ct);
        }

        /// <summary>Opens an obligation for every incomplete item (the due date stays the one it got first), closes the ones that became complete. Returns the codes opened now.</summary>
        private async Task<List<string>> SyncObligationsAsync(int workOrderId, int productId, ReadinessResult r, int? exceptionId, DateTime today, CancellationToken ct)
        {
            IReadOnlyDictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            int dueDays = OpsReadinessCatalog.Days(st, OpsReadinessCatalog.DueDays, 7);
            List<OpsReadinessObligation> existing = await _uow.Set<OpsReadinessObligation>().Where(o => !o.Deleted && o.WorkOrderId == workOrderId).ToListAsync(ct);
            var opened = new List<string>();
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (ReadinessItemDto item in r.Items)
            {
                OpsReadinessObligation ob = existing.FirstOrDefault(o => o.ItemCode == item.Code);
                bool incomplete = item.Status == OpsReadiness.MissingStatus || item.Status == OpsReadiness.Partial;
                string detail = string.Join("\n", item.Offenders.Take(10).Select(o => o.Text));
                if (incomplete)
                {
                    if (ob == null)
                    {
                        _uow.Set<OpsReadinessObligation>().Add(new OpsReadinessObligation
                        {
                            WorkOrderId = workOrderId, ProductId = productId, ItemCode = item.Code, Title = item.Title, OwnerUnit = item.Owner, Hard = item.Hard, MissingCount = item.MissingCount, Detail = detail,
                            DueDate = today.AddDays(dueDays), ExceptionId = item.Hard ? exceptionId : null, Status = OpsObligationStatus.Open, OpenedAt = now, CreateDate = now, ModifyDate = now,
                        });
                        opened.Add(item.Code);
                    }
                    else
                    {
                        if (ob.Status == OpsObligationStatus.Closed) { ob.Status = OpsObligationStatus.Open; ob.ClosedAt = null; ob.DueDate = today.AddDays(dueDays); ob.EscalatedAt = null; ob.LastNotifiedAt = null; opened.Add(item.Code); }
                        ob.MissingCount = item.MissingCount; ob.Detail = detail; ob.Hard = item.Hard; ob.ModifyDate = now;
                        if (item.Hard && exceptionId.HasValue && ob.ExceptionId == null) ob.ExceptionId = exceptionId;
                    }
                }
                else if (ob != null && ob.Status == OpsObligationStatus.Open)
                {
                    ob.Status = OpsObligationStatus.Closed; ob.ClosedAt = now; ob.ModifyDate = now;
                    await _notify.CloseAsync("rdy:" + N(workOrderId) + ":" + item.Code, "تکمیل شد", ct);
                }
            }
            await _uow.SaveChangesAsync(ct);
            return opened;
        }

        private async Task NotifyOwnersAsync(int workOrderId, string woCode, List<ReadinessItemDto> items, string reason, CancellationToken ct)
        {
            foreach (var group in items.GroupBy(i => i.Owner))
            {
                string roles = OpsReadinessCatalog.OwnerRoles(group.Key) + ",PMO";
                foreach (ReadinessItemDto i in group)
                {
                    await _notify.NotifyAsync(new NotifyRequest
                    {
                        SourceKey = "rdy:" + N(workOrderId) + ":" + i.Code, Module = "opsdata", SourceType = "readiness", SourceId = workOrderId, Kind = OfInboxKind.Task, Priority = i.Hard ? OfUrgency.Urgent : OfUrgency.Normal,
                        Title = $"اطلاعات مهندسی ناقص ({reason}) — دستور کار {woCode}: {i.Title} ({N(i.MissingCount)} مورد)", Link = "/product-bank/products", Roles = roles,
                    }, ct);
                }
            }
        }

        // ================================================================================================ daily

        public async Task<string> DailyAsync(DateTime today, CancellationToken ct)
        {
            IReadOnlyDictionary<string, decimal> st = await QualitySettingKeys.LoadAsync(_uow, ct);
            if (st.TryGetValue(OpsReadinessCatalog.MasterSwitch, out decimal master) && master == 0m) return "readiness=off";
            int escalateDays = OpsReadinessCatalog.Days(st, OpsReadinessCatalog.EscalateDays, 3);
            var baselines = await _uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent).Select(b => new { b.WorkOrderId, b.ProductId }).OrderBy(b => b.WorkOrderId).Take(300).ToListAsync(ct);
            int newGaps = 0, reminded = 0, escalated = 0;
            var batch = new BatchCache();
            foreach (var b in baselines)
            {
                ReadinessResult r = await ComputeAsync(b.ProductId, b.WorkOrderId, ct, batch);
                int? exceptionId = await _uow.Set<OpsReadinessException>().AsNoTracking().Where(e => !e.Deleted && e.WorkOrderId == b.WorkOrderId && e.Status == OpsReadinessExceptionStatus.Used).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
                List<string> opened = await SyncObligationsAsync(b.WorkOrderId, b.ProductId, r, exceptionId, today, ct);
                string woCode = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == b.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
                if (opened.Count > 0)
                {
                    newGaps += opened.Count;
                    await NotifyOwnersAsync(b.WorkOrderId, woCode, r.Items.Where(i => opened.Contains(i.Code)).ToList(), "تغییر مدارک پس از شروع", ct);
                }
                List<OpsReadinessObligation> open = await _uow.Set<OpsReadinessObligation>().Where(o => !o.Deleted && o.WorkOrderId == b.WorkOrderId && o.Status == OpsObligationStatus.Open).ToListAsync(ct);
                foreach (OpsReadinessObligation ob in open.Where(o => o.DueDate.Date < today.Date && (o.LastNotifiedAt == null || o.LastNotifiedAt.Value.LocalDateTime.Date < today.Date)))
                {
                    int late = (int)(today.Date - ob.DueDate.Date).TotalDays;
                    await _notify.NotifyAsync(new NotifyRequest
                    {
                        SourceKey = "rdy:" + N(ob.WorkOrderId) + ":" + ob.ItemCode, Module = "opsdata", SourceType = "readiness", SourceId = ob.WorkOrderId, Kind = OfInboxKind.Alert, Priority = OfUrgency.Urgent,
                        Title = $"تعهد تکمیل اطلاعات مهندسی دستور کار {woCode} ({ob.Title}) {N(late)} روز دیرکرد دارد.", Link = "/product-bank/products/" + N(ob.ProductId), Roles = OpsReadinessCatalog.OwnerRoles(ob.OwnerUnit) + ",PMO",
                    }, ct);
                    ob.LastNotifiedAt = DateTimeOffset.Now; reminded++;
                    if (ob.Hard && late >= escalateDays && ob.EscalatedAt == null)   // only a missing hard item goes up to the CEO; a soft gap is the owner's reminder
                    {
                        await _notify.NotifyAsync(new NotifyRequest
                        {
                            SourceKey = "rdyc:" + N(ob.WorkOrderId) + ":" + ob.ItemCode, Module = "opsdata", SourceType = "readiness-escalation", SourceId = ob.WorkOrderId, Kind = OfInboxKind.Alert, Priority = OfUrgency.Urgent,
                            Title = $"ارجاع به مدیرعامل: اطلاعات مهندسی دستور کار {woCode} ({ob.Title}) بیش از {N(escalateDays)} روز دیرکرد دارد.", Link = "/product-bank/products/" + N(ob.ProductId), Roles = "CEO",
                        }, ct);
                        ob.EscalatedAt = DateTimeOffset.Now; escalated++;
                    }
                }
                await _uow.SaveChangesAsync(ct);
            }
            return $"readiness: projects={N(baselines.Count)}; newGaps={N(newGaps)}; reminded={N(reminded)}; escalated={N(escalated)}";
        }

        // ================================================================================================ summary

        public async Task<ResponseDto<ReadinessSummaryDto>> SummaryAsync(CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(Readers)) return Bad<ReadinessSummaryDto>("شما مجوز دیدن این گزارش را ندارید.");
            var baselines = await _uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent).Select(b => new { b.WorkOrderId, b.ProductId }).OrderBy(b => b.WorkOrderId).Take(200).ToListAsync(ct);
            List<int> woIds = baselines.Select(b => b.WorkOrderId).ToList();
            Dictionary<int, string> codes = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            List<int> pIds = baselines.Select(b => b.ProductId).Distinct().ToList();
            Dictionary<int, string> pCodes = await _uow.Set<PbProduct>().AsNoTracking().Where(p => pIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
            var obligations = await _uow.Set<OpsReadinessObligation>().AsNoTracking().Where(o => !o.Deleted && o.Status == OpsObligationStatus.Open && woIds.Contains(o.WorkOrderId)).Select(o => new { o.WorkOrderId, o.DueDate }).ToListAsync(ct);
            var sum = new ReadinessSummaryDto { AsOf = DateTime.Today };
            var batch = new BatchCache();
            foreach (var b in baselines)
            {
                ReadinessResult r = await ComputeAsync(b.ProductId, b.WorkOrderId, ct, batch);
                var mine = obligations.Where(o => o.WorkOrderId == b.WorkOrderId).ToList();
                sum.Items.Add(new ReadinessProjectDto
                {
                    WorkOrderId = b.WorkOrderId, WorkOrderCode = codes.TryGetValue(b.WorkOrderId, out string c) ? c : N(b.WorkOrderId), ProductCode = pCodes.TryGetValue(b.ProductId, out string pc) ? pc : null, Percent = r.Percent,
                    HardMissing = r.HardMissing, SoftMissing = r.SoftMissing, OpenObligations = mine.Count, OverdueObligations = mine.Count(o => o.DueDate.Date < DateTime.Today), Link = "/product-bank/products/" + N(b.ProductId),
                });
            }
            sum.Projects = sum.Items.Count;
            sum.AveragePercent = sum.Items.Count == 0 ? 100 : (int)Math.Round(sum.Items.Average(i => i.Percent), MidpointRounding.AwayFromZero);
            sum.OpenObligations = sum.Items.Sum(i => i.OpenObligations);
            sum.OverdueObligations = sum.Items.Sum(i => i.OverdueObligations);
            return ResponseDtoHelper.Ok(sum);
        }

        public async Task<ResponseDto<List<ReadinessObligationDto>>> ObligationsAsync(int? workOrderId, bool openOnly, CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(Readers)) return Bad<List<ReadinessObligationDto>>("شما مجوز دیدن این گزارش را ندارید.");
            IQueryable<OpsReadinessObligation> q = _uow.Set<OpsReadinessObligation>().AsNoTracking().Where(o => !o.Deleted);
            if (workOrderId.HasValue) q = q.Where(o => o.WorkOrderId == workOrderId.Value);
            if (openOnly) q = q.Where(o => o.Status == OpsObligationStatus.Open);
            List<OpsReadinessObligation> rows = await q.OrderBy(o => o.DueDate).ThenBy(o => o.Id).Take(500).ToListAsync(ct);
            List<int> woIds = rows.Select(o => o.WorkOrderId).Distinct().ToList();
            Dictionary<int, string> codes = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => woIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            return ResponseDtoHelper.Ok(rows.Select(o => new ReadinessObligationDto
            {
                Id = o.Id, WorkOrderId = o.WorkOrderId, WorkOrderCode = codes.TryGetValue(o.WorkOrderId, out string c) ? c : null, ItemCode = o.ItemCode, Title = o.Title, OwnerTitle = OpsReadinessCatalog.OwnerTitle(o.OwnerUnit),
                Hard = o.Hard, MissingCount = o.MissingCount, Detail = o.Detail, DueDate = o.DueDate, DueDateText = InventoryText.PersianDate(o.DueDate), Overdue = o.Status == OpsObligationStatus.Open && o.DueDate.Date < DateTime.Today,
                Closed = o.Status == OpsObligationStatus.Closed,
            }).ToList());
        }

        // ================================================================================================ exceptions (PMO and CEO)

        public async Task<ResponseDto<ReadinessExceptionDto>> RequestExceptionAsync(RequestReadinessExceptionDto dto, CancellationToken ct)
        {
            if (Me == 0 || IsSeat || !_user.IsInRole(Requesters)) return Bad<ReadinessExceptionDto>("شما مجوز این کار را ندارید.");
            if (dto == null || dto.WorkOrderId <= 0 || dto.ProductId <= 0) return Bad<ReadinessExceptionDto>("اطلاعات ارسال‌شده کامل نیست.");
            string why = (dto.Justification ?? string.Empty).Trim();
            if (why.Length < 10) return Bad<ReadinessExceptionDto>("توجیه استثنا را بنویسید (دست‌کم ۱۰ نویسه): چرا پروژه باید بدون اطلاعات کامل مهندسی شروع شود؟");
            if (why.Length > 1000) return Bad<ReadinessExceptionDto>("توجیه نباید از ۱۰۰۰ نویسه بیشتر باشد.");
            if (!await _uow.Set<WorkOrder>().AnyAsync(w => w.Id == dto.WorkOrderId && !w.Deleted, ct)) return ResponseDtoHelper.NotFound<ReadinessExceptionDto>("دستور کار پیدا نشد.");
            if (await _uow.Set<PbProjectBaseline>().AnyAsync(b => !b.Deleted && b.IsCurrent && b.WorkOrderId == dto.WorkOrderId, ct)) return Bad<ReadinessExceptionDto>("برای این دستور کار پایه‌ریزی انجام شده است.");
            ReadinessResult r = await ComputeAsync(dto.ProductId, dto.WorkOrderId, ct);
            if (r.HardGaps.Count == 0) return Bad<ReadinessExceptionDto>("اطلاعات مهندسی کامل است؛ استثنا لازم نیست.");
            if (await _uow.Set<OpsReadinessException>().AnyAsync(e => !e.Deleted && e.WorkOrderId == dto.WorkOrderId && (e.Status == OpsReadinessExceptionStatus.Pending || e.Status == OpsReadinessExceptionStatus.Approved), ct))
                return Bad<ReadinessExceptionDto>("برای این دستور کار درخواست استثنای باز وجود دارد.");
            DateTimeOffset now = DateTimeOffset.Now;
            var ex = new OpsReadinessException
            {
                WorkOrderId = dto.WorkOrderId, ProductId = dto.ProductId, ItemCodes = string.Join(",", r.HardGaps.Select(g => g.Code)), Justification = why, RequestedByUserId = Me, RequestedAt = now,
                Status = OpsReadinessExceptionStatus.Pending, CreateDate = now, ModifyDate = now, CreatorUserId = Me, ModifierUserId = Me,
            };
            _uow.Set<OpsReadinessException>().Add(ex);
            await _uow.SaveChangesAsync(ct);
            await _notify.NotifyAsync(new NotifyRequest
            {
                SourceKey = "rdx:" + N(ex.Id), Module = "opsdata", SourceType = "readiness-exception", SourceId = ex.Id, Kind = OfInboxKind.Approval, Priority = OfUrgency.Urgent,
                Title = $"درخواست استثنا برای شروع پروژه با اطلاعات مهندسی ناقص ({ex.ItemCodes})", Link = "/ops-data/quality?tab=readiness", Roles = "PMO,CEO",
            }, ct);
            return ResponseDtoHelper.Ok(await MapAsync(ex, ct));
        }

        public async Task<ResponseDto<ReadinessExceptionDto>> DecideExceptionAsync(DecideReadinessExceptionDto dto, CancellationToken ct)
        {
            if (Me == 0 || IsSeat) return Bad<ReadinessExceptionDto>("شما مجوز این کار را ندارید.");
            if (dto == null || dto.Id <= 0) return Bad<ReadinessExceptionDto>("اطلاعات ارسال‌شده کامل نیست.");
            OpsReadinessException ex = await _uow.Set<OpsReadinessException>().FirstOrDefaultAsync(e => e.Id == dto.Id && !e.Deleted, ct);
            if (ex == null) return ResponseDtoHelper.NotFound<ReadinessExceptionDto>("درخواست استثنا پیدا نشد.");
            if (ex.Status != OpsReadinessExceptionStatus.Pending) return Bad<ReadinessExceptionDto>("درباره این درخواست قبلاً تصمیم گرفته شده است.");
            if (ex.RequestedByUserId == Me) return Bad<ReadinessExceptionDto>("درخواست‌دهنده نمی‌تواند استثنای خودش را تأیید یا رد کند (تفکیک وظایف).");
            bool asCeo = _user.IsInRole("CEO");
            bool asPmo = _user.IsInRole("PMO");
            if (!asCeo && !asPmo) return Bad<ReadinessExceptionDto>("فقط مدیر PMO و مدیرعامل درباره این استثنا تصمیم می‌گیرند.");
            string reason = (dto.Reason ?? string.Empty).Trim();
            if (!dto.Approve && reason.Length == 0) return Bad<ReadinessExceptionDto>("دلیل رد را بنویسید.");
            if (reason.Length > 500) return Bad<ReadinessExceptionDto>("توضیح نباید از ۵۰۰ نویسه بیشتر باشد.");
            DateTimeOffset now = DateTimeOffset.Now;
            if (!dto.Approve)
            {
                ex.Status = OpsReadinessExceptionStatus.Rejected; ex.RejectedByUserId = Me; ex.RejectedAt = now; ex.RejectReason = reason;
            }
            else
            {
                // the CEO seat of the approval is filled first when the person is the CEO; a person is never both approvers
                bool takeCeo = asCeo && ex.CeoUserId == null && ex.PmoUserId != Me;
                bool takePmo = !takeCeo && asPmo && ex.PmoUserId == null && ex.CeoUserId != Me;
                if (!takeCeo && !takePmo) return Bad<ReadinessExceptionDto>("این تأیید را قبلاً داده‌اید یا جایگاه شما (مدیر PMO / مدیرعامل) پر شده است؛ تأیید دوم باید شخص دیگری بدهد.");
                if (takeCeo) { ex.CeoUserId = Me; ex.CeoAt = now; ex.CeoNote = reason.Length == 0 ? null : reason; }
                else { ex.PmoUserId = Me; ex.PmoAt = now; ex.PmoNote = reason.Length == 0 ? null : reason; }
                if (ex.CeoUserId.HasValue && ex.PmoUserId.HasValue && ex.CeoUserId != ex.PmoUserId) ex.Status = OpsReadinessExceptionStatus.Approved;
            }
            ex.ModifierUserId = Me; ex.ModifyDate = now;
            try { await _uow.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Bad<ReadinessExceptionDto>("همزمان تصمیم دیگری روی این درخواست ثبت شد؛ صفحه را تازه کنید و دوباره تصمیم بگیرید."); }
            if (ex.Status != OpsReadinessExceptionStatus.Pending) await _notify.CloseAsync("rdx:" + N(ex.Id), ex.Status == OpsReadinessExceptionStatus.Approved ? "تأیید شد" : "رد شد", ct);
            return ResponseDtoHelper.Ok(await MapAsync(ex, ct));
        }

        public async Task<ResponseDto<List<ReadinessExceptionDto>>> ExceptionsAsync(int? workOrderId, CancellationToken ct)
        {
            if (Me == 0 || !_user.IsInRole(Readers)) return Bad<List<ReadinessExceptionDto>>("شما مجوز دیدن این گزارش را ندارید.");
            IQueryable<OpsReadinessException> q = _uow.Set<OpsReadinessException>().AsNoTracking().Where(e => !e.Deleted);
            if (workOrderId.HasValue) q = q.Where(e => e.WorkOrderId == workOrderId.Value);
            List<OpsReadinessException> rows = await q.OrderByDescending(e => e.Id).Take(200).ToListAsync(ct);
            var list = new List<ReadinessExceptionDto>();
            foreach (OpsReadinessException e in rows) list.Add(await MapAsync(e, ct));
            return ResponseDtoHelper.Ok(list);
        }

        private async Task<ReadinessExceptionDto> MapAsync(OpsReadinessException e, CancellationToken ct)
        {
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(_uow, new int?[] { e.RequestedByUserId, e.PmoUserId, e.CeoUserId }, ct);
            string wo = await _uow.Set<WorkOrder>().AsNoTracking().Where(w => w.Id == e.WorkOrderId).Select(w => w.Code).FirstOrDefaultAsync(ct);
            bool can = e.Status == OpsReadinessExceptionStatus.Pending && !IsSeat && e.RequestedByUserId != Me
                && ((_user.IsInRole("CEO") && e.CeoUserId == null && e.PmoUserId != Me) || (_user.IsInRole("PMO") && e.PmoUserId == null && e.CeoUserId != Me));
            return new ReadinessExceptionDto
            {
                Id = e.Id, WorkOrderId = e.WorkOrderId, WorkOrderCode = wo, ProductId = e.ProductId, ItemCodes = e.ItemCodes, Justification = e.Justification, RequestedByUserId = e.RequestedByUserId,
                RequestedByName = names.Name(e.RequestedByUserId), RequestedAtText = OpsMap.Stamp(e.RequestedAt), Status = e.Status.ToString(),
                StatusTitle = e.Status == OpsReadinessExceptionStatus.Pending ? "در انتظار تأیید مدیر PMO و مدیرعامل" : e.Status == OpsReadinessExceptionStatus.Approved ? "تأییدشده (منتظر پایه‌ریزی)" : e.Status == OpsReadinessExceptionStatus.Used ? "به‌کار رفته" : "ردشده",
                PmoByName = names.Name(e.PmoUserId), PmoAtText = e.PmoAt.HasValue ? OpsMap.Stamp(e.PmoAt.Value) : null, CeoByName = names.Name(e.CeoUserId), CeoAtText = e.CeoAt.HasValue ? OpsMap.Stamp(e.CeoAt.Value) : null,
                RejectReason = e.RejectReason, CanDecide = can,
            };
        }
    }
}
