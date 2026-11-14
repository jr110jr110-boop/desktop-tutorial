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
        public int Station => ProcStepCommon.StationNumber;
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
