using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.ProjectControl;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 4 (planning), priority 4, phase 4d: from an approved resource plan the planning agent PROPOSES the lines of the cost baseline by resource class (RES.COST.PROPOSE); the owner of the cost baseline
    // (planning, PMO) accepts or rejects them through the existing cost-baseline path (RES.COST.REVIEW). The agent never writes a cost-baseline line and never changes an earned-value number.

    internal static class ResCostFacts
    {
        public const string Roles = ResFacts.PlanRoles;

        public static string ReviewLink(int contractId) => "/project-control/resources?tab=performance&contract=" + contractId.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The contracts that have an approved resource plan (the newest one), a cost baseline that is still editable (no baseline in force), at least one line of the plan priced by an approved rate, and no proposal yet for that plan
        /// (a rejected or a superseded one counts: the agent does not make the same proposal again for the same plan).
        /// </summary>
        public static IQueryable<int> NeedingProposal(IUnitOfWork uow) =>
            uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted && p.Status == PcResourcePlanStatus.Approved
                    && !uow.Set<PcResourcePlan>().Any(n => !n.Deleted && n.ContractId == p.ContractId && n.Status == PcResourcePlanStatus.Approved && n.Version > p.Version)
                    && !uow.Set<PcContractTerms>().Any(t => !t.Deleted && t.ContractId == p.ContractId && t.CurrentBaselineId != null)
                    && !uow.Set<PcResourceCostProposal>().Any(x => !x.Deleted && x.PlanId == p.Id)
                    && uow.Set<PcActivityResource>().Any(x => x.PlanId == p.Id && uow.Set<PcResourceRate>().Any(r => !r.Deleted && r.Status == PcRateStatus.Approved && r.ResourceId == (x.Resource.RateResourceId ?? x.ResourceId))))
                .Select(p => p.ContractId).Distinct();

        public static IQueryable<int> WithOpenProposals(IUnitOfWork uow) =>
            uow.Set<PcResourceCostProposal>().AsNoTracking().Where(x => !x.Deleted && x.Status == PcCostProposalStatus.Proposed).Select(x => x.ContractId).Distinct();
    }

    /// <summary>RES.COST.PROPOSE: the planning agent prepares the proposed cost-baseline lines by resource class (quantity x approved standard rate) from the approved resource plan. Nothing is written to the cost baseline.</summary>
    internal sealed class ResCostProposeStep : ResStep
    {
        public override string Code => "RES.COST.PROPOSE";
        public override string Title => "ایجنت برنامه‌ریزی: پیشنهاد ردیف‌های خط مبنای هزینه از طرح منابع تاییدشده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ResFacts.PlanRoles;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> need = ResCostFacts.NeedingProposal(uow);
            List<ContractEntity> rows = await ConFacts.Live(uow, today).Where(c => need.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, "طرح منابع تایید شده و خط مبنای هزینه هنوز قابل ویرایش است؛ ایجنت ردیف‌های پیشنهادی هزینهٔ منابع را می‌سازد.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<int> r = await services.GetRequiredService<IPcResourcePerformanceService>().ProposeCostAsync(item.TargetId, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            string text = r.Data == 0
                ? $"برای قرارداد {item.TargetNumber} ردیف پیشنهادی ساخته نشد (مبلغ قابل‌محاسبه‌ای نیست: نرخ مصوب ندارد یا گروه حساس است)."
                : $"ایجنت {r.Data} ردیف پیشنهادی هزینهٔ منابع برای قرارداد {item.TargetNumber} ساخت؛ تصمیم با برنامه‌ریز یا PMO است.";
            return AgentStepResult.Done("Contract", item.TargetId, item.TargetNumber, text);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResCostFacts.Roles, Link = ResCostFacts.ReviewLink(item.ContractId ?? item.TargetId), Title = "پیشنهاد هزینه منابع " + item.TargetNumber };
    }

    /// <summary>RES.COST.REVIEW (gate): the owner of the cost baseline (planning, PMO) accepts or rejects each proposed line; an accepted line is written through the existing cost-baseline path.</summary>
    internal sealed class ResCostReviewStep : ResStep
    {
        public override string Code => "RES.COST.REVIEW";
        public override string Title => "تصمیم دربارهٔ ردیف‌های پیشنهادی هزینهٔ منابع برای خط مبنای هزینه";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> open = ResCostFacts.WithOpenProposals(uow);
            List<ContractEntity> rows = await ConFacts.Live(uow, today).Where(c => open.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, "ردیف‌های پیشنهادی هزینهٔ منابع منتظر تصمیم‌اند؛ هر ردیف را بپذیرید (از مسیر ردیف‌های خط مبنای هزینه ثبت می‌شود) یا با علت رد کنید.", ResCostFacts.Roles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResCostFacts.Roles, Link = ResCostFacts.ReviewLink(item.ContractId ?? item.TargetId), Title = "ردیف‌های پیشنهادی هزینه منابع " + item.TargetNumber };
    }
}
