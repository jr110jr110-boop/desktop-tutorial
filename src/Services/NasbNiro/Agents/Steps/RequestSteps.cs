using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectBalance;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectBalance;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.InventoryDtos;
using ViewModels.NasbNiro.ProcurementDtos;
using ViewModels.NasbNiro.ProjectBalanceDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // The purchase request: made from the reorder list or the BOM shortage by an agent, submitted by the agent that made it (when every line has an item and a price), approved by planning and
    // management (people), returned with a reason to the agent that made it, which makes the next revision; a person fixes what the agent cannot (prices, the reason of a return).

    /// <summary>WH.PR.REORDER: the warehouse agent drafts one purchase request for the items under their reorder point (the work of the WH-REORDER seat, which stands down while this step is on).</summary>
    internal sealed class ReorderStep : ProcStep
    {
        public const int MaxLines = WhReorderSeatJob.MaxLines;
        private readonly IServiceProvider _sp;

        public ReorderStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        public override string Code => WhReorderSeatJob.SupersedingStep;
        public override string AgentCode => AgentCodes.Wh;
        public override string Title => "ایجنت انبار: پیش‌نویس درخواست خرید از فهرست نقطه سفارش مجدد";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "WAREHOUSE,WHSUPERVISOR";

        public static int RunId(DateTime day) => day.Year * 10000 + day.Month * 100 + day.Day;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            // the list of the warehouse screen (read only): one run of the day while an item is under its point and not on an open request
            ResponseDto<List<ReorderRowDto>> list = await _sp.GetRequiredService<IWarehouseReorderService>().GetAsync(new ReorderQuery(), ct);
            if (!list.Success) throw new InvalidOperationException(list.Message);
            int count = list.Data.Count(r => !r.OpenRequest && r.SuggestedQuantity > 0);
            if (count == 0) return new List<AgentWorkItem>();
            return new List<AgentWorkItem> { Item("ReorderRun", RunId(today), today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, null, $"{count} کالا از نقطه سفارش مجدد کمتر است و درخواستی برای آن‌ها باز نیست.") };
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            var reorder = services.GetRequiredService<IWarehouseReorderService>();
            var procurement = services.GetRequiredService<IProcurementService>();
            ResponseDto<List<ReorderRowDto>> list = await reorder.GetAsync(new ReorderQuery(), ct);
            if (!list.Success) return ProcStepCommon.Refusal(list);
            List<ReorderRowDto> todo = list.Data.Where(r => !r.OpenRequest && r.SuggestedQuantity > 0).OrderBy(r => r.ItemCode, StringComparer.Ordinal).Take(MaxLines).ToList();
            if (todo.Count == 0) return AgentStepResult.Done(null, null, null, "کالایی زیر نقطه سفارش مجدد بدون درخواست باز نبود.");
            var dto = new SavePurchaseRequestDto
            {
                Source = PurchaseRequestSource.ReorderPoint, Kind = PurchaseKind.Goods, RequestDate = DateTime.Today, Submit = false,
                Description = "پیش‌نویس خودکار ایجنت انبار: موجودی کالاهای فهرست‌شده از نقطه سفارش مجدد کمتر است. مقدار پیشنهادی از فهرست نقطه سفارش انبار گرفته شده و موجودی و مقدار در راه را حساب می‌کند. ایجنت فقط ارسال می‌کند؛ تایید با برنامه‌ریزی و مدیریت است.",
            };
            // the estimate of each line comes from the system: the latest order, then the latest accepted quote; with none of them the save method takes the cost of the item
            Dictionary<int, ProcPrices.ItemPrice> prices = await ProcPrices.ResolveAsync(services.GetRequiredService<IUnitOfWork>(), todo.Select(t => t.ItemId).ToList(), ct);
            foreach (ReorderRowDto i in todo)
                dto.Lines.Add(new SavePurchaseRequestLineDto
                {
                    ItemId = i.ItemId, Unit = i.Unit, Quantity = i.SuggestedQuantity, EstimatedUnitPrice = prices.TryGetValue(i.ItemId, out ProcPrices.ItemPrice price) ? price.Price : (decimal?)null,
                    Description = $"{i.ItemTitle}: موجودی {AiSeatTexts.Num(i.OnHand)} {i.Unit} کمتر از نقطه سفارش {AiSeatTexts.Num(i.ReorderPoint)} است{(i.OnOrder > 0 ? $"؛ {AiSeatTexts.Num(i.OnOrder)} {i.Unit} در راه است" : string.Empty)}",
                });
            ResponseDto<PurchaseRequestDto> saved = await procurement.SaveRequestAsync(dto, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PurchaseRequest", saved.Data.Id, saved.Data.Number, $"ایجنت انبار پیش‌نویس {saved.Data.Number} را با {todo.Count} ردیف ساخت.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/inventory/reorder", Title = "ساخت درخواست خرید از فهرست نقطه سفارش مجدد" };
    }

    /// <summary>PLAN.PR.BOM: the planning agent runs the balance of a project whose latest run has a shortage without a request line and makes the draft requests.</summary>
    internal sealed class BomShortageStep : ProcStep
    {
        public override string Code => "PLAN.PR.BOM";
        public override string AgentCode => AgentCodes.Plan;
        public override string Title => "ایجنت برنامه‌ریزی: پیش‌نویس درخواست خرید از کمبود مواد BOM";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<ProjectMaterialBalance> runs = uow.Set<ProjectMaterialBalance>().AsNoTracking();
            IQueryable<ProjectMaterialBalanceLine> lines = uow.Set<ProjectMaterialBalanceLine>().AsNoTracking();
            IQueryable<PbProjectBaseline> baselines = uow.Set<PbProjectBaseline>().AsNoTracking();
            IQueryable<InventoryItem> items = uow.Set<InventoryItem>().AsNoTracking();
            // the latest run of a current baseline, with a short coded line of an active item that no request line covers
            var found = await runs
                .Where(r => !r.Deleted && r.ShortLineCount > 0
                            && r.RunNumber == runs.Where(x => x.BaselineId == r.BaselineId && !x.Deleted).Max(x => x.RunNumber)
                            && baselines.Any(b => b.Id == r.BaselineId && b.IsCurrent && !b.Deleted)
                            && lines.Any(l => l.BalanceId == r.Id && !l.Deleted && l.ItemId != null && l.ShortQty > 0 && l.Kind != BalanceRequirementKind.NotCoded && l.PurchaseRequestLineId == null
                                              && items.Any(i => i.Id == l.ItemId && i.IsActive && !i.Deleted)))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap)
                .Select(r => new { r.BaselineId, r.WorkOrderId, r.ContractId, r.ShortLineCount }).ToListAsync(ct);
            List<int> workOrderIds = found.Select(f => f.WorkOrderId).Distinct().ToList();
            Dictionary<int, string> codes = await uow.Set<Entities.Model.NasbNiro.Contracts.WorkOrder>().AsNoTracking().Where(w => workOrderIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.Code, ct);
            return found.Select(f => Item("PbProjectBaseline", f.BaselineId, codes.TryGetValue(f.WorkOrderId, out string code) ? code : null, f.ContractId, f.WorkOrderId,
                $"{f.ShortLineCount} ردیف کمبود مواد در آخرین بالانس پروژه درخواست خرید ندارد.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            if (!item.WorkOrderId.HasValue) return AgentStepResult.Refused("دستور ساخت این پروژه مشخص نیست.");
            if (item.ContractId.HasValue)
            {
                // the draft requests of a project wait for the accepted contract review (the service says so too): not a failure, the step waits
                string review = await ProjectControl.PcGates.RequireReviewAsync(uow, item.ContractId.Value, ct);
                if (review != null) return AgentStepResult.Waiting(review);
            }
            ResponseDto<BalanceRunBatchDto> batch = await services.GetRequiredService<IProjectBalanceService>().RunAsync(new BalanceRunRequestDto { WorkOrderId = item.WorkOrderId, CreateDrafts = true }, ct);
            if (!batch.Success) return ProcStepCommon.Refusal(batch);
            List<BalanceDraftRequestDto> requests = batch.Data.Drafts.SelectMany(d => d.Requests).ToList();
            if (requests.Count == 0)
            {
                string note = string.Join(" | ", batch.Data.Notes.Concat(batch.Data.Drafts.SelectMany(d => d.Notes)));
                return string.IsNullOrWhiteSpace(note) ? AgentStepResult.Done(null, null, null, "کمبودی که درخواست خرید بخواهد نبود.") : AgentStepResult.Waiting(note);
            }
            BalanceDraftRequestDto first = requests[0];
            return AgentStepResult.Done("PurchaseRequest", first.Id, first.Number, $"ایجنت برنامه‌ریزی {requests.Count} پیش‌نویس درخواست خرید از کمبود مواد ساخت (نخستین: {first.Number}).");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/project-balance", Title = "اجرای بالانس مواد و ساخت پیش‌نویس درخواست خرید " + item.TargetNumber };
    }

    /// <summary>The base of the three steps that look at the drafts an agent made.</summary>
    internal abstract class SeatRequestStep : ProcStep
    {
        private readonly string _code;
        private readonly string _agent;

        protected SeatRequestStep(string code, string agentCode)
        {
            _code = code; _agent = agentCode;
        }

        public override string Code => _code;
        public override string AgentCode => _agent;

        protected static string Link(AgentWorkItem item) => "/procurement/requests/" + item.TargetId.ToString(CultureInfo.InvariantCulture);

        /// <summary>The requests of the given status that this agent made itself (the creator is its service user) and nobody has taken over.</summary>
        protected async Task<IQueryable<PurchaseRequest>> OwnAsync(IUnitOfWork uow, PurchaseRequestStatus status, CancellationToken ct)
        {
            int seat = await ProcStepCommon.SeatUserIdAsync(uow, _agent, ct);
            return uow.Set<PurchaseRequest>().AsNoTracking().Where(r => !r.Deleted && r.Status == status && seat != 0 && r.CreatorUserId == seat);
        }

        protected static List<AgentWorkItem> ToItems<T>(IEnumerable<T> rows, Func<T, AgentWorkItem> map) => rows.Select(map).ToList();
    }

    /// <summary>PROC.PR.SUBMIT.*: the agent that made a draft submits it for approval when every line has an item and a price above zero; otherwise it waits and PROC.PR.PRICE asks a person.</summary>
    internal sealed class SubmitRequestStep : SeatRequestStep
    {
        public const string NotPriced = "قیمت برآوردی ردیف‌ها کامل نیست";
        private readonly string _fallback;

        public SubmitRequestStep(string code, string agentCode, string fallbackRoles) : base(code, agentCode)
        {
            _fallback = fallbackRoles;
        }

        public override string Title => "ایجنت: ارسال پیش‌نویس درخواست خرید برای تایید";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => _fallback;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            // a revision (PreviousRequestId set) is for a person: PROC.PR.FIX; a draft that a person took over has another creator
            IQueryable<PurchaseRequest> own = await OwnAsync(uow, PurchaseRequestStatus.Draft, ct);
            IQueryable<int> orderItems = ProcPrices.ItemsWithOrderPrice(uow), quoteItems = ProcPrices.ItemsWithQuotePrice(uow);
            // a draft that PROC.PR.PRICE.AUTO can still price is not submitted yet (the next round submits it)
            var rows = await own.Where(r => r.PreviousRequestId == null && r.SupersededByRequestId == null
                                            && !r.Lines.Any(l => l.ItemId != null && (l.EstimatedUnitPrice == null || l.EstimatedUnitPrice <= 0) && (orderItems.Contains(l.ItemId.Value) || quoteItems.Contains(l.ItemId.Value))))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap)
                .Select(r => new { r.Id, r.Number, r.ContractId, r.WorkOrderId, r.EstimatedAmount }).ToListAsync(ct);
            return ToItems(rows, r => Item("PurchaseRequest", r.Id, r.Number, r.ContractId, r.WorkOrderId, "پیش‌نویس ساخته‌شده توسط ایجنت منتظر ارسال است."));
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            PurchaseRequest request = await uow.Set<PurchaseRequest>().AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == item.TargetId && !r.Deleted, ct);
            if (request == null || request.Status != PurchaseRequestStatus.Draft) return AgentStepResult.Waiting("وضعیت درخواست تغییر کرده است.");
            // a request without prices has an estimate of zero and would skip the management approval: it is not submitted until a person prices it
            if (request.Lines.Count == 0 || request.Lines.Any(l => !l.ItemId.HasValue || (l.EstimatedUnitPrice ?? 0) <= 0)) return AgentStepResult.Waiting(NotPriced);
            if (request.ContractId.HasValue)
            {
                string review = await ProjectControl.PcGates.RequireReviewAsync(uow, request.ContractId.Value, ct);
                if (review != null) return AgentStepResult.Waiting(review);
            }
            ResponseDto<PurchaseRequestDto> done = await services.GetRequiredService<IProcurementService>().RequestActionAsync(new ProcurementActionDto { Id = request.Id, Action = "submit" }, ct);
            if (!done.Success) return ProcStepCommon.Refusal(done);
            return AgentStepResult.Done("PurchaseRequest", request.Id, request.Number, $"ایجنت درخواست خرید {request.Number} را برای تایید برنامه‌ریزی ارسال کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = Link(item), Title = "ارسال درخواست خرید " + item.TargetNumber };
    }

    /// <summary>PROC.PR.PRICE.AUTO.*: the agent that made a draft fills the estimated price of its lines from the latest order or accepted quote of the item (a person prices only what the system has no price for).</summary>
    internal sealed class PriceAutoStep : SeatRequestStep
    {
        private readonly string _fallback;

        public PriceAutoStep(string code, string agentCode, string fallbackRoles) : base(code, agentCode)
        {
            _fallback = fallbackRoles;
        }

        public override string Title => "ایجنت: قیمت‌گذاری برآوردی ردیف‌های پیش‌نویس از سفارش‌ها و پیشنهادهای قبلی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => _fallback;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<PurchaseRequest> own = await OwnAsync(uow, PurchaseRequestStatus.Draft, ct);
            IQueryable<int> orderItems = ProcPrices.ItemsWithOrderPrice(uow), quoteItems = ProcPrices.ItemsWithQuotePrice(uow);
            var rows = await own.Where(r => r.PreviousRequestId == null && r.SupersededByRequestId == null
                                            && r.Lines.Any(l => l.ItemId != null && (l.EstimatedUnitPrice == null || l.EstimatedUnitPrice <= 0) && (orderItems.Contains(l.ItemId.Value) || quoteItems.Contains(l.ItemId.Value))))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.Number, r.ContractId, r.WorkOrderId }).ToListAsync(ct);
            return ToItems(rows, r => Item("PurchaseRequest", r.Id, r.Number, r.ContractId, r.WorkOrderId, "ردیف‌های بدون قیمت برآوردی پیش‌نویس، سابقه قیمت در سفارش یا پیشنهاد دارند."));
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            PurchaseRequest r = await uow.Set<PurchaseRequest>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == item.TargetId && !x.Deleted, ct);
            if (r == null || r.Status != PurchaseRequestStatus.Draft) return AgentStepResult.Waiting("وضعیت درخواست تغییر کرده است.");
            List<PurchaseRequestLine> lines = r.Lines.OrderBy(l => l.RowNumber).ToList();
            Dictionary<int, ProcPrices.ItemPrice> prices = await ProcPrices.ResolveAsync(uow, lines.Where(l => l.ItemId.HasValue && (l.EstimatedUnitPrice ?? 0) <= 0).Select(l => l.ItemId.Value).Distinct().ToList(), ct);
            if (prices.Count == 0) return AgentStepResult.Waiting("سابقه قیمتی برای کالاهای بدون قیمت پیدا نشد.");
            // the whole draft goes back through the save method (the agent is its creator); only the empty prices change
            var dto = new SavePurchaseRequestDto
            {
                Id = r.Id, Source = r.Source, Kind = r.Kind, RequestDate = r.RequestDate, WorkOrderId = r.WorkOrderId, ContractId = r.ContractId, CostCenter = r.CostCenter, MaterialRequestId = r.MaterialRequestId,
                Description = r.Description, Priority = r.Priority, FormType = Enum.IsDefined(typeof(PurchaseFormType), r.FormType) ? r.FormType : (PurchaseFormType?)null, Submit = false,
            };
            var sources = new List<string>();
            foreach (PurchaseRequestLine l in lines)
            {
                decimal? price = l.EstimatedUnitPrice;
                if ((price ?? 0) <= 0 && l.ItemId.HasValue && prices.TryGetValue(l.ItemId.Value, out ProcPrices.ItemPrice p)) { price = p.Price; sources.Add(p.Source); }
                dto.Lines.Add(new SavePurchaseRequestLineDto
                {
                    Id = l.Id, ItemId = l.ItemId, Description = l.Description, Unit = l.Unit, Quantity = l.Quantity, NeededBy = l.NeededBy, EstimatedUnitPrice = price,
                    TechnicalRequirements = l.TechnicalRequirements, BuyOrderId = l.BuyOrderId, MaterialRequestLineId = l.MaterialRequestLineId,
                });
            }
            ResponseDto<PurchaseRequestDto> saved = await services.GetRequiredService<IProcurementService>().SaveRequestAsync(dto, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PurchaseRequest", r.Id, r.Number, $"ایجنت قیمت برآوردی {sources.Count} ردیف را از سابقه سیستم نوشت ({string.Join("؛ ", sources.Distinct().Take(3))}).");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = Link(item), Title = "قیمت‌گذاری برآوردی پیش‌نویس " + item.TargetNumber };
    }

    /// <summary>PROC.PR.PRICE.*: an agent's draft has a line without an item or a price: a planner prices it and submits it (and becomes its maker, so another person approves).</summary>
    internal sealed class PriceRequestStep : SeatRequestStep
    {
        public PriceRequestStep(string code, string agentCode) : base(code, agentCode) { }

        public override string Title => "تکمیل قیمت برآوردی پیش‌نویس ایجنت و ارسال درخواست خرید";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<PurchaseRequest> own = await OwnAsync(uow, PurchaseRequestStatus.Draft, ct);
            IQueryable<int> orderItems = ProcPrices.ItemsWithOrderPrice(uow), quoteItems = ProcPrices.ItemsWithQuotePrice(uow);
            // only the lines with no price anywhere in the system are for a person: the agent prices the others (PROC.PR.PRICE.AUTO)
            var rows = await own.Where(r => r.PreviousRequestId == null && r.SupersededByRequestId == null
                                            && r.Lines.Any(l => l.ItemId == null || ((l.EstimatedUnitPrice == null || l.EstimatedUnitPrice <= 0) && !orderItems.Contains(l.ItemId.Value) && !quoteItems.Contains(l.ItemId.Value))))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap)
                .Select(r => new { r.Id, r.Number, r.ContractId, r.WorkOrderId, Missing = r.Lines.Count(l => l.ItemId == null || ((l.EstimatedUnitPrice == null || l.EstimatedUnitPrice <= 0) && !orderItems.Contains(l.ItemId.Value) && !quoteItems.Contains(l.ItemId.Value))) }).ToListAsync(ct);
            var titles = new Dictionary<int, string>();
            foreach (var r in rows) titles[r.Id] = string.Join("، ", (await ProcPrices.UnpricedTitlesAsync(uow, r.Id, ct)).Take(8));
            return ToItems(rows, r => Item("PurchaseRequest", r.Id, r.Number, r.ContractId, r.WorkOrderId,
                $"کالاهای بدون سابقه قیمت (نه سفارش، نه پیشنهاد، نه بهای موجودی): {titles[r.Id]}. "+
                $"{r.Missing} ردیف قیمت برآوردی یا کد کالا ندارد؛ ایجنت درخواست بدون قیمت را ارسال نمی‌کند. قیمت را وارد کنید و درخواست را خودتان ارسال کنید."));
        }

        // a person who edits the draft of an agent must hold one of the requesting roles; planning prices it (ProcurementService.SaveRequestAsync, the draft of a seat)
        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "PLANNING", Link = Link(item), Title = "قیمت‌گذاری و ارسال درخواست خرید " + item.TargetNumber };
    }

    /// <summary>PROC.PR.APPROVE: planning approves, and for a medium or major purchase management too (never the person who made the request). The send-back is the return action of the request.</summary>
    internal sealed class ApproveRequestStep : ProcStep, IAgentReturnable
    {
        public override string Code => "PROC.PR.APPROVE";
        public override string AgentCode => AgentCodes.Plan;
        public override string Title => "تایید درخواست خرید";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            Dictionary<string, decimal> s = await ProcStepCommon.SettingsAsync(uow, ct);
            var rows = await uow.Set<PurchaseRequest>().AsNoTracking().Where(r => !r.Deleted && r.Status == PurchaseRequestStatus.Submitted)
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap)
                .Select(r => new { r.Id, r.Number, r.ContractId, r.WorkOrderId, r.EstimatedAmount, Planning = r.PlanningApprovedAt != null, Management = r.ManagementApprovedAt != null }).ToListAsync(ct);
            return rows.Select(r =>
            {
                // the same rule as RequestActionAsync: planning always, management from the medium limit, and what is already approved is not asked again
                string roles = ProcStepCommon.Roles(r.Planning ? null : ProcurementRoles.Planning, !r.Management && r.EstimatedAmount >= s[ProcurementSettingKeys.MediumFrom] ? ProcurementRoles.Management : null);
                return Item("PurchaseRequest", r.Id, r.Number, r.ContractId, r.WorkOrderId, $"درخواست خرید به مبلغ برآوردی {ProcStepCommon.Num(r.EstimatedAmount)} ریال منتظر تایید است.", roles);
            }).Where(i => i.GateRoles != null).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo
        {
            Roles = item.GateRoles ?? ProcurementRoles.Planning, Link = "/procurement/requests/" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "تایید درخواست خرید " + item.TargetNumber, HasOfficeInboxItem = true,
        };

        public async Task<ResponseDto<bool>> ReturnAsync(AgentWorkItem item, string reason, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PurchaseRequestDto> returned = await services.GetRequiredService<IProcurementService>().RequestActionAsync(new ProcurementActionDto { Id = item.TargetId, Action = "return", Note = reason }, ct);
            return returned.Success ? ResponseDtoHelper.Ok(true) : ResponseDtoHelper.BadRequest<bool>(returned.Message);
        }
    }

    /// <summary>PROC.PR.REVISE.*: a request that planning or management returned goes back to the agent that made it, which makes the next revision (a copy; the correction itself is for a person: PROC.PR.FIX).</summary>
    internal sealed class ReviseRequestStep : SeatRequestStep
    {
        private readonly string _fallback;

        public ReviseRequestStep(string code, string agentCode, string fallbackRoles) : base(code, agentCode)
        {
            _fallback = fallbackRoles;
        }

        public override string Title => "ایجنت: نسخه جدید درخواست خرید برگشت‌خورده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => _fallback;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<PurchaseRequest> own = await OwnAsync(uow, PurchaseRequestStatus.Returned, ct);
            var rows = await own.Where(r => r.SupersededByRequestId == null).OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.Number, r.ContractId, r.WorkOrderId, r.ReturnReason }).ToListAsync(ct);
            return ToItems(rows, r => Item("PurchaseRequest", r.Id, r.Number, r.ContractId, r.WorkOrderId, "برگشت‌خورده: " + r.ReturnReason));
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PurchaseRequestDto> revised = await services.GetRequiredService<IProcurementService>().RequestActionAsync(new ProcurementActionDto { Id = item.TargetId, Action = "revise" }, ct);
            if (!revised.Success) return ProcStepCommon.Refusal(revised);
            return AgentStepResult.Done("PurchaseRequest", revised.Data.Id, revised.Data.Number, $"ایجنت نسخه جدید {revised.Data.Number} را از درخواست برگشت‌خورده {item.TargetNumber} ساخت؛ اصلاح آن با انسان است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = Link(item), Title = "ساخت نسخه جدید درخواست خرید " + item.TargetNumber };
    }

    /// <summary>PROC.PR.FIX.*: the revision of a returned request is a draft; a person makes the correction the reason asks for and submits it (and so becomes its maker: another person approves).</summary>
    internal sealed class FixRequestStep : ProcStep
    {
        private readonly string _code;
        private readonly string _agent;
        private readonly PurchaseRequestSource? _source;

        /// <param name="source">The reorder revisions belong to the warehouse agent; null takes every other source (the planning agent).</param>
        public FixRequestStep(string code, string agentCode, PurchaseRequestSource? source)
        {
            _code = code; _agent = agentCode; _source = source;
        }

        public override string Code => _code;
        public override string AgentCode => _agent;
        public override string Title => "اصلاح درخواست خرید برگشت‌خورده و ارسال دوباره";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<PurchaseRequest> all = uow.Set<PurchaseRequest>().AsNoTracking();
            IQueryable<PurchaseRequest> drafts = all.Where(r => !r.Deleted && r.Status == PurchaseRequestStatus.Draft && r.PreviousRequestId != null && all.Any(p => p.Id == r.PreviousRequestId && p.ReturnReason != null));
            drafts = _source.HasValue ? drafts.Where(r => r.Source == _source.Value) : drafts.Where(r => r.Source != PurchaseRequestSource.ReorderPoint);
            var rows = await drafts.OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.Number, r.ContractId, r.WorkOrderId, Reason = all.Where(p => p.Id == r.PreviousRequestId).Select(p => p.ReturnReason).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("PurchaseRequest", r.Id, r.Number, r.ContractId, r.WorkOrderId, "دلیل برگشت: " + r.Reason)).ToList();
        }

        // the draft of an agent is edited by the requesting roles (ProcurementService.SaveRequestAsync); the creator of a revision is the agent, so the first person who saves it takes it over
        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.Requesters, Link = "/procurement/requests/" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "اصلاح و ارسال دوباره درخواست خرید " + item.TargetNumber };
    }
}
