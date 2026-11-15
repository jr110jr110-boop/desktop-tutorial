using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Procurement;

namespace Services.NasbNiro.Agents.Steps
{
    /// <summary>
    /// Station 6 (procurement): the steps of the chain from the reorder list or the BOM shortage to the paid invoice. Every step finds its documents from their state alone and
    /// either does the step by calling the existing service method as the agent (the data layer refuses a seat the signature of a decision), or shows the person who must act the page of the document.
    /// </summary>
    public static class ProcurementSteps
    {
        /// <summary>Registers every step of the station. The same class is registered once per agent where a step belongs to the agent that made the document (distinct codes).</summary>
        public static IServiceCollection AddProcurementSteps(this IServiceCollection services)
        {
            // requests: the warehouse agent (reorder) and the planning agent (BOM) make and submit; planning and management approve; a returned request is revised by the agent that made it
            services.AddScoped<IAgentStep, ReorderStep>();
            services.AddScoped<IAgentStep, BomShortageStep>();
            services.AddScoped<IAgentStep>(sp => new SubmitRequestStep("PROC.PR.SUBMIT.WH", AgentCodes.Wh, "WAREHOUSE,WHSUPERVISOR"));
            services.AddScoped<IAgentStep>(sp => new SubmitRequestStep("PROC.PR.SUBMIT.PLAN", AgentCodes.Plan, "PLANNING,PMO"));
            services.AddScoped<IAgentStep>(sp => new PriceRequestStep("PROC.PR.PRICE.WH", AgentCodes.Wh));
            services.AddScoped<IAgentStep>(sp => new PriceRequestStep("PROC.PR.PRICE.PLAN", AgentCodes.Plan));
            services.AddScoped<IAgentStep>(sp => new PriceAutoStep("PROC.PR.PRICE.AUTO.WH", AgentCodes.Wh, "WAREHOUSE,WHSUPERVISOR"));
            services.AddScoped<IAgentStep>(sp => new PriceAutoStep("PROC.PR.PRICE.AUTO.PLAN", AgentCodes.Plan, "PLANNING,PMO"));
            services.AddScoped<IAgentStep, ApproveRequestStep>();
            services.AddScoped<IAgentStep>(sp => new ReviseRequestStep("PROC.PR.REVISE.WH", AgentCodes.Wh, "WAREHOUSE,WHSUPERVISOR"));
            services.AddScoped<IAgentStep>(sp => new ReviseRequestStep("PROC.PR.REVISE.PLAN", AgentCodes.Plan, "PLANNING,PMO"));
            services.AddScoped<IAgentStep>(sp => new FixRequestStep("PROC.PR.FIX.WH", AgentCodes.Wh, PurchaseRequestSource.ReorderPoint));
            services.AddScoped<IAgentStep>(sp => new FixRequestStep("PROC.PR.FIX.PLAN", AgentCodes.Plan, null));

            // inquiry: opened by the procurement agent, quotes by the buyer, technical and planning gates, selection by the agent (or by the buyer when it cannot), committee
            services.AddScoped<IAgentStep, OpenInquiryStep>();
            services.AddScoped<IAgentStep, QuotesInputStep>();
            services.AddScoped<IAgentStep, TechnicalGateStep>();
            services.AddScoped<IAgentStep, PlanningGateStep>();
            services.AddScoped<IAgentStep, SelectQuoteStep>();
            services.AddScoped<IAgentStep, SelectManualStep>();
            services.AddScoped<IAgentStep, CommitteeGateStep>();

            // order
            services.AddScoped<IAgentStep, DraftOrderStep>();
            services.AddScoped<IAgentStep, OrderRequirementsStep>();
            services.AddScoped<IAgentStep, CompleteOrderStep>();
            services.AddScoped<IAgentStep, ApproveOrderStep>();
            services.AddScoped<IAgentStep, ReleaseOrderStep>();
            services.AddScoped<IAgentStep, ReceiveGoodsStep>();
            services.AddScoped<IAgentStep, AcceptServiceStep>();

            // invoice and payment
            services.AddScoped<IAgentStep, RegisterInvoiceStep>();
            services.AddScoped<IAgentStep, ApproveInvoiceStep>();
            services.AddScoped<IAgentStep, DraftPaymentStep>();
            services.AddScoped<IAgentStep, ConfirmDeductionsStep>();
            services.AddScoped<IAgentStep, ApprovePaymentStep>();
            services.AddScoped<IAgentStep, PayStep>();
            return services;
        }
    }

    /// <summary>The price an agent writes into the estimate of a draft line, from what the system already knows (B0): the latest live purchase-order line of the item, then the latest technically accepted quote line, then the stock valuation (the average cost of the item, which the save method of the request also falls back to).</summary>
    internal static class ProcPrices
    {
        private static readonly PurchaseOrderStatus[] LiveOrder =
        {
            PurchaseOrderStatus.Approved, PurchaseOrderStatus.Released, PurchaseOrderStatus.PartlyReceived, PurchaseOrderStatus.Received, PurchaseOrderStatus.Closed,
        };

        /// <summary>The ids of the items that have a price in a live order (rial) or in an accepted quote: the SQL side of "an agent can price this line".</summary>
        public static IQueryable<int> ItemsWithOrderPrice(IUnitOfWork uow) =>
            uow.Set<PurchaseOrderLine>().AsNoTracking()
                .Where(l => l.ItemId != null && l.UnitPrice > 0 && !l.PurchaseOrder.Deleted && l.PurchaseOrder.Currency == null && LiveOrder.Contains(l.PurchaseOrder.Status))
                .Select(l => l.ItemId.Value);

        public static IQueryable<int> ItemsWithQuotePrice(IUnitOfWork uow) =>
            uow.Set<SupplierQuoteLine>().AsNoTracking()
                .Where(l => l.RequestLine.ItemId != null && l.UnitPrice > 0 && l.Quote.TechnicallyAccepted && !l.Quote.Inquiry.Deleted)
                .Select(l => l.RequestLine.ItemId.Value);

        public sealed class ItemPrice
        {
            public decimal Price { get; set; }
            public string Source { get; set; }
        }

        /// <summary>The price of each item that has one in an order or a quote (the cost table is left to the save method of the request, which uses it when no price is given).</summary>
        public static async Task<Dictionary<int, ItemPrice>> ResolveAsync(IUnitOfWork uow, ICollection<int> itemIds, CancellationToken ct)
        {
            var result = new Dictionary<int, ItemPrice>();
            if (itemIds.Count == 0) return result;
            var orders = await uow.Set<PurchaseOrderLine>().AsNoTracking()
                .Where(l => l.ItemId != null && itemIds.Contains(l.ItemId.Value) && l.UnitPrice > 0 && !l.PurchaseOrder.Deleted && l.PurchaseOrder.Currency == null && LiveOrder.Contains(l.PurchaseOrder.Status))
                .OrderByDescending(l => l.PurchaseOrder.OrderDate).ThenByDescending(l => l.Id)
                .Select(l => new { Item = l.ItemId.Value, l.UnitPrice, l.PurchaseOrder.Number }).Take(2000).ToListAsync(ct);
            foreach (var o in orders)
                if (!result.ContainsKey(o.Item)) result[o.Item] = new ItemPrice { Price = o.UnitPrice, Source = "آخرین سفارش خرید " + o.Number };
            List<int> rest = itemIds.Where(i => !result.ContainsKey(i)).ToList();
            if (rest.Count == 0) return result;
            var quotes = await uow.Set<SupplierQuoteLine>().AsNoTracking()
                .Where(l => l.RequestLine.ItemId != null && rest.Contains(l.RequestLine.ItemId.Value) && l.UnitPrice > 0 && l.Quote.TechnicallyAccepted && !l.Quote.Inquiry.Deleted)
                .OrderByDescending(l => l.Quote.QuoteDate).ThenByDescending(l => l.Id)
                .Select(l => new { Item = l.RequestLine.ItemId.Value, l.UnitPrice, l.Quote.Inquiry.Number }).Take(2000).ToListAsync(ct);
            foreach (var q in quotes)
                if (!result.ContainsKey(q.Item)) result[q.Item] = new ItemPrice { Price = q.UnitPrice, Source = "آخرین پیشنهاد قابل قبول فنی استعلام " + q.Number };
            return result;
        }

        /// <summary>The items of the lines that have no price in the system at all (no order, no accepted quote, no cost): the only ones a person has to price.</summary>
        public static async Task<List<string>> UnpricedTitlesAsync(IUnitOfWork uow, int requestId, CancellationToken ct)
        {
            var lines = await uow.Set<PurchaseRequestLine>().AsNoTracking().Where(l => l.PurchaseRequestId == requestId).OrderBy(l => l.RowNumber)
                .Select(l => new { l.ItemId, l.Description, l.EstimatedUnitPrice }).ToListAsync(ct);
            Dictionary<int, ItemPrice> known = await ResolveAsync(uow, lines.Where(l => l.ItemId.HasValue).Select(l => l.ItemId.Value).Distinct().ToList(), ct);
            return lines.Where(l => (l.EstimatedUnitPrice ?? 0) <= 0 && !(l.ItemId.HasValue && known.ContainsKey(l.ItemId.Value))).Select(l => l.Description ?? ("#" + l.ItemId)).ToList();
        }
    }

    /// <summary>What the steps of the station share. A step reads documents with AsNoTracking only and calls services; it never writes through the unit of work.</summary>
    internal static class ProcStepCommon
    {
        /// <summary>The most documents one step finds in one pass (oldest first); the rest follow in the next passes.</summary>
        public const int Cap = 200;

        public const int StationNumber = 6;

        /// <summary>The settings of the procurement thresholds, read without writing the defaults (the coordinator reads them and may not write the table).</summary>
        public static async Task<Dictionary<string, decimal>> SettingsAsync(IUnitOfWork uow, CancellationToken ct)
        {
            Dictionary<string, decimal> result = ProcurementSettingKeys.Defaults().ToDictionary(d => d.Key, d => d.Value);
            List<ProcurementSetting> rows = await uow.Set<ProcurementSetting>().AsNoTracking().ToListAsync(ct);
            foreach (ProcurementSetting row in rows) result[row.Key] = row.Value;
            return result;
        }

        /// <summary>The user id of the service user of an agent seat, or 0 when the seat has not been provisioned yet.</summary>
        public static async Task<int> SeatUserIdAsync(IUnitOfWork uow, string agentCode, CancellationToken ct)
        {
            string name = AiSeatNames.UserName(agentCode);
            int id = await uow.Set<Entities.Identity.User>().AsNoTracking().Where(u => u.UserName == name).Select(u => u.Id).FirstOrDefaultAsync(ct);
            return id;
        }

        /// <summary>The user ids of every agent or assistant seat (user names that start with <c>seat.</c>): the documents they made are "made by an agent". A small set (one user per seat).</summary>
        public static async Task<int[]> AllSeatUserIdsAsync(IUnitOfWork uow, CancellationToken ct)
        {
            string prefix = AiSeatNames.UserPrefix;
            return (await uow.Set<Entities.Identity.User>().AsNoTracking().Where(u => u.UserName.StartsWith(prefix)).Select(u => u.Id).Take(200).ToListAsync(ct)).ToArray();
        }

        public static string Roles(params string[] lists) => AgentText.NormalizeRoles(string.Join(",", lists.Where(l => !string.IsNullOrWhiteSpace(l))));

        /// <summary>The message of a refusal of a service (Persian, as the service wrote it).</summary>
        public static AgentStepResult Refusal<T>(ResponseDto<T> response, string fallback = null) =>
            AgentStepResult.Refused(string.IsNullOrWhiteSpace(response?.Message) ? (fallback ?? "خدمت درخواست را نپذیرفت.") : response.Message);

        public static string Num(decimal value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The base of the steps of the station: station 6, no run unless the step is an agent step.</summary>
    internal abstract class ProcStep : IAgentStep
    {
        public abstract string Code { get; }
        public virtual int Station => ProcStepCommon.StationNumber;
        public abstract string AgentCode { get; }
        public abstract string Title { get; }
        public abstract AgentStepKind Kind { get; }
        public virtual string FallbackRoles => null;

        public abstract Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct);

        public virtual Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            throw new InvalidOperationException("گام " + Code + " توسط ایجنت اجرا نمی‌شود.");

        public abstract AgentGateInfo GateInfo(AgentWorkItem item);

        protected static AgentWorkItem Item(string type, int id, string number, int? contractId, int? workOrderId, string summary, string roles = null) =>
            new AgentWorkItem { TargetType = type, TargetId = id, TargetNumber = number, ContractId = contractId, WorkOrderId = workOrderId, Summary = summary, GateRoles = roles };
    }
}
