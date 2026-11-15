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
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 4 (planning), priority 4: the resource plan of a project. The planning agent drafts it from the resource pool (RES.PLAN.DRAFT, deterministic, no LLM). People do what is theirs:
    // they supply the resources the pool lacks, review and submit the draft, and the project manager (one named person) approves it. The standard rates are proposed by the finance manager and approved by a second person.

    public static class ResourceSteps
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddResourceSteps(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            services.AddScoped<IAgentStep, ResPlanDraftStep>();
            services.AddScoped<IAgentStep, ResPoolInputStep>();
            services.AddScoped<IAgentStep, ResPlanReviewStep>();
            services.AddScoped<IAgentStep, ResPlanApproveStep>();
            services.AddScoped<IAgentStep, ResPlanFixStep>();
            services.AddScoped<IAgentStep, ResRateInputStep>();
            services.AddScoped<IAgentStep, ResRateApproveStep>();
            services.AddScoped<IAgentStep, ResLoadCheckStep>();
            services.AddScoped<IAgentStep, ResLoadResolveStep>();
            services.AddScoped<IAgentStep, ResLevelProposeStep>();
            services.AddScoped<IAgentStep, ResLevelDecideStep>();
            services.AddScoped<IAgentStep, ResLevelConfirmStep>();
            return services;
        }
    }

    /// <summary>What the steps of the resource plan read from the state.</summary>
    internal static class ResFacts
    {
        public const string PlanRoles = "PLANNING,PMO";
        public const string RateMakerRoles = "FINANCEMANAGER";
        public const string RateApproverRoles = "FINANCEMANAGER,CEO";

        public static string PlanLink(int contractId) => "/project-control/resources?contract=" + contractId.ToString(CultureInfo.InvariantCulture);
        public static string RatesLink(int? resourceId = null) => "/project-control/resources?tab=rates" + (resourceId.HasValue ? "&resource=" + resourceId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);

        /// <summary>The contracts that have a plan of any status (so the agent drafts a plan once, before the baseline).</summary>
        public static IQueryable<int> WithPlan(IUnitOfWork uow) => uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted).Select(p => p.ContractId);

        public static IQueryable<int> WithOpenPlan(IUnitOfWork uow) =>
            uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted && (p.Status == PcResourcePlanStatus.Draft || p.Status == PcResourcePlanStatus.Submitted || p.Status == PcResourcePlanStatus.Returned)).Select(p => p.ContractId);

        public static IQueryable<int> WithDurations(IUnitOfWork uow) => uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.DurationDays > 0).Select(a => a.ContractId);

        /// <summary>The pool has an active resource that is not linked to a person and names the unit it serves: the agent has something to match.</summary>
        public static Task<bool> PoolHasMatchAsync(IUnitOfWork uow, CancellationToken ct) =>
            uow.Set<PcResource>().AsNoTracking().AnyAsync(r => !r.Deleted && r.IsActive && r.EmployeeId == null && r.ResponsibleUnit != null, ct);

        /// <summary>Contracts with a leaf activity (it has days) whose responsible unit is served by an active resource of the pool (not linked to a person): the agent has something to match there.</summary>
        public static IQueryable<int> WithMatch(IUnitOfWork uow)
        {
            IQueryable<string> units = uow.Set<PcResource>().AsNoTracking().Where(r => !r.Deleted && r.IsActive && r.EmployeeId == null && r.ResponsibleUnit != null).Select(r => r.ResponsibleUnit);
            return uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.DurationDays > 0 && a.ResponsibleUnit != null && units.Contains(a.ResponsibleUnit)).Select(a => a.ContractId);
        }

        /// <summary>The step setting of the agent step that drafts the plan: when a person switched it off, the baseline agent is not held back for the draft.</summary>
        public static async Task<bool> DraftingEnabledAsync(IUnitOfWork uow, CancellationToken ct) =>
            !await uow.Set<AgentStepSetting>().AsNoTracking().AnyAsync(x => !x.Deleted && x.StepCode == "RES.PLAN.DRAFT" && !x.Enabled, ct);

        /// <summary>Contracts before the baseline whose resource plan the agent should draft now (also used to hold the baseline agent back for one run): a matching resource exists and no plan was made yet.</summary>
        public static IQueryable<int> DraftBeforeBaseline(IUnitOfWork uow, IQueryable<int> open) =>
            DraftBeforeBaselineCore(open, WithMatch(uow), WithPlan(uow));

        private static IQueryable<int> DraftBeforeBaselineCore(IQueryable<int> open, IQueryable<int> matching, IQueryable<int> withPlan) => open.Where(id => matching.Contains(id) && !withPlan.Contains(id));

        /// <summary>Contracts with an approved change request that reopens the plan and no plan opened for it yet.</summary>
        public static IQueryable<int> DraftAfterChange(IUnitOfWork uow) =>
            uow.Set<PcChangeRequest>().AsNoTracking().Where(c => !c.Deleted && c.Decision == PcChangeDecision.Approve && c.Status == PcChangeStatus.Implementing
                && uow.Set<PcBaseline>().Any(b => !b.Deleted && b.ChangeRequestId == c.Id && b.Status == PcBaselineStatus.Approved)
                && !uow.Set<PcResourcePlan>().Any(p => !p.Deleted && p.ChangeRequestId == c.Id)).Select(c => c.ContractId).Distinct();

        public sealed class PlanGap
        {
            public int PlanId { get; set; }
            public int ContractId { get; set; }
            public ContractEntity Contract { get; set; }
            public PcResourcePlanStatus Status { get; set; }
            public string ReturnReason { get; set; }
            public int MakerUserId { get; set; }
            public string EditorUserIds { get; set; }
            public int Unassigned { get; set; }
            public List<string> Units { get; set; } = new List<string>();
        }

        internal sealed class PlanRow
        {
            public int Id { get; set; }
            public int ContractId { get; set; }
            public string ReturnReason { get; set; }
            public int MakerUserId { get; set; }
            public string EditorUserIds { get; set; }
        }

        internal sealed class ActivityRow
        {
            public int Id { get; set; }
            public int ContractId { get; set; }
            public string ResponsibleUnit { get; set; }
        }

        internal sealed class PlanActivityRow
        {
            public int PlanId { get; set; }
            public int ActivityId { get; set; }
        }

        internal sealed class ManagerRow
        {
            public int ContractId { get; set; }
            public int Manager { get; set; }
        }

        // the queries of the plan steps, one place so that the SQL translation test builds exactly these
        public static IQueryable<PlanRow> PlanRows(IUnitOfWork uow, PcResourcePlanStatus status, DateTime today)
        {
            IQueryable<int> live = ConFacts.Live(uow, today).Select(c => c.Id);
            return uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted && p.Status == status && live.Contains(p.ContractId)).OrderBy(p => p.Id).Take(ProcStepCommon.Cap)
                .Select(p => new PlanRow { Id = p.Id, ContractId = p.ContractId, ReturnReason = p.ReturnReason, MakerUserId = p.MakerUserId, EditorUserIds = p.EditorUserIds });
        }

        public static IQueryable<ActivityRow> ActivityRows(IUnitOfWork uow, int[] contractIds) =>
            uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.DurationDays > 0 && contractIds.Contains(a.ContractId)).OrderBy(a => a.Id).Select(a => new ActivityRow { Id = a.Id, ContractId = a.ContractId, ResponsibleUnit = a.ResponsibleUnit });

        public static IQueryable<PlanActivityRow> AssignedRows(IUnitOfWork uow, int[] planIds) =>
            uow.Set<PcActivityResource>().AsNoTracking().Where(x => planIds.Contains(x.PlanId)).Select(x => new PlanActivityRow { PlanId = x.PlanId, ActivityId = x.ActivityId }).Distinct();

        public static IQueryable<PlanActivityRow> SkippedRows(IUnitOfWork uow, int[] planIds) =>
            uow.Set<PcResourcePlanSkip>().AsNoTracking().Where(x => planIds.Contains(x.PlanId)).Select(x => new PlanActivityRow { PlanId = x.PlanId, ActivityId = x.ActivityId });

        public static IQueryable<ManagerRow> ManagerRows(IUnitOfWork uow, int[] contractIds) =>
            uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.ProjectManagerUserId != null && contractIds.Contains(t.ContractId)).OrderBy(t => t.Id).Select(t => new ManagerRow { ContractId = t.ContractId, Manager = t.ProjectManagerUserId.Value });

        /// <summary>The plans in a status (of live contracts, then capped) with the activities of their contract that have neither a resource nor the «بدون منبع» mark. A fixed number of queries whatever the number of plans.</summary>
        public static async Task<List<PlanGap>> GapsAsync(IUnitOfWork uow, PcResourcePlanStatus status, DateTime today, CancellationToken ct)
        {
            List<PlanRow> plans = await PlanRows(uow, status, today).ToListAsync(ct);
            if (plans.Count == 0) return new List<PlanGap>();
            int[] planIds = plans.Select(p => p.Id).ToArray();
            int[] contractIds = plans.Select(p => p.ContractId).Distinct().ToArray();
            Dictionary<int, ContractEntity> contracts = (await ConFacts.Live(uow, today).Where(c => contractIds.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
            List<ActivityRow> acts = await ActivityRows(uow, contractIds).ToListAsync(ct);
            HashSet<(int, int)> handled = new HashSet<(int, int)>((await AssignedRows(uow, planIds).ToListAsync(ct)).Select(x => (x.PlanId, x.ActivityId)));
            handled.UnionWith((await SkippedRows(uow, planIds).ToListAsync(ct)).Select(x => (x.PlanId, x.ActivityId)));
            var result = new List<PlanGap>();
            foreach (PlanRow p in plans)
            {
                if (!contracts.TryGetValue(p.ContractId, out ContractEntity contract)) continue;
                var open = acts.Where(a => a.ContractId == p.ContractId && !handled.Contains((p.Id, a.Id))).ToList();
                result.Add(new PlanGap
                {
                    PlanId = p.Id, ContractId = p.ContractId, Contract = contract, Status = status, ReturnReason = p.ReturnReason, MakerUserId = p.MakerUserId, EditorUserIds = p.EditorUserIds, Unassigned = open.Count,
                    Units = open.Select(a => string.IsNullOrWhiteSpace(a.ResponsibleUnit) ? "بدون واحد" : a.ResponsibleUnit.Trim().ToUpperInvariant()).Distinct().OrderBy(u => u, StringComparer.Ordinal).ToList(),
                });
            }
            return result;
        }

        public static AgentWorkItem PlanItem(PlanGap g, string summary, string roles = null) => ConFacts.Item(g.Contract, "PcResourcePlan", g.PlanId, summary, roles);
    }

    internal abstract class ResStep : PlanStep
    {
    }

    /// <summary>RES.PLAN.DRAFT: the draft resource plan of a project, made from the resource pool by the fixed rule of the plan service (the responsible unit of each activity against the unit of the resource).</summary>
    internal sealed class ResPlanDraftStep : ResStep
    {
        public override string Code => "RES.PLAN.DRAFT";
        public override string Title => "ایجنت برنامه‌ریزی: پیش‌نویس طرح منابع پروژه از استخر منابع";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ResFacts.PlanRoles;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await ResFacts.PoolHasMatchAsync(uow, ct)) return new List<AgentWorkItem>();
            IQueryable<int> before = ResFacts.DraftBeforeBaseline(uow, Open(uow, today).Select(c => c.Id));
            IQueryable<int> durations = ResFacts.WithDurations(uow), openPlans = ResFacts.WithOpenPlan(uow);
            IQueryable<int> afterChange = ResFacts.DraftAfterChange(uow).Where(id => durations.Contains(id) && !openPlans.Contains(id));
            List<ContractEntity> rows = await ConFacts.Live(uow, today).Where(c => before.Contains(c.Id) || afterChange.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, "فعالیت‌های برنامه و استخر منابع ثبت شده است ولی طرح منابع پروژه هنوز پیش‌نویس نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PcResourcePlanDto> r = await services.GetRequiredService<IPcResourceService>().DraftPlanAsync(item.TargetId, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            PcResourcePlanDto plan = r.Data;
            string rest = plan.UnassignedCount > 0 ? $"؛ برای {plan.UnassignedCount} فعالیت منبعی در استخر پیدا نشد و برنامه‌ریز باید آن را کامل کند" : "؛ برنامه‌ریز آن را بازبینی و ارسال می‌کند";
            return AgentStepResult.Done("PcResourcePlan", plan.Id, item.TargetNumber, $"ایجنت پیش‌نویس طرح منابع قرارداد {item.TargetNumber} را با {plan.Assignments.Count} ردیف منبع ساخت{rest}.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.PlanRoles, Link = ResFacts.PlanLink(item.ContractId ?? item.TargetId), Title = "طرح منابع " + item.TargetNumber };
    }

    /// <summary>RES.POOL.INPUT: the draft leaves activities without a resource; the planner adds the resources the pool lacks, assigns them by hand or marks the rest «بدون منبع».</summary>
    internal sealed class ResPoolInputStep : ResStep
    {
        public override string Code => "RES.POOL.INPUT";
        public override string Title => "تکمیل منابع فعالیت‌های بدون منبع در طرح منابع";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<ResFacts.PlanGap> gaps = await ResFacts.GapsAsync(uow, PcResourcePlanStatus.Draft, today, ct);
            return gaps.Where(g => g.Unassigned > 0).Select(g => ResFacts.PlanItem(g,
                $"برای {g.Unassigned} فعالیت منبعی ثبت نشده است (واحدها: {string.Join("، ", g.Units)})؛ منبع مناسب را به استخر اضافه یا دستی تخصیص دهید، یا باقی‌مانده را «بدون منبع» علامت بزنید.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.PlanRoles, Link = ResFacts.PlanLink(item.ContractId ?? 0), Title = "منابع فعالیت‌ها " + item.TargetNumber };
    }

    /// <summary>RES.PLAN.REVIEW (gate): the planner reviews and edits the draft and submits it; the agent never submits (assignments are a human planning judgement).</summary>
    internal sealed class ResPlanReviewStep : ResStep
    {
        public override string Code => "RES.PLAN.REVIEW";
        public override string Title => "بازبینی و ارسال طرح منابع پروژه";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<ResFacts.PlanGap> gaps = await ResFacts.GapsAsync(uow, PcResourcePlanStatus.Draft, today, ct);
            return gaps.Where(g => g.Unassigned == 0).Select(g => ResFacts.PlanItem(g, "پیش‌نویس طرح منابع آماده بازبینی است؛ برنامه‌ریز آن را بازبینی، در صورت نیاز اصلاح و برای مدیر پروژه ارسال می‌کند.", ResFacts.PlanRoles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.PlanRoles, Link = ResFacts.PlanLink(item.ContractId ?? 0), Title = "بازبینی طرح منابع " + item.TargetNumber };
    }

    /// <summary>RES.PLAN.APPROVE (gate): the project manager OF THE CONTRACT approves the plan (a named person); a contract without one, or whose manager made the plan, is decided by the PMO.</summary>
    internal sealed class ResPlanApproveStep : ResStep
    {
        public override string Code => "RES.PLAN.APPROVE";
        public override string Title => "تایید طرح منابع پروژه توسط مدیر پروژه";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<ResFacts.PlanGap> plans = await ResFacts.GapsAsync(uow, PcResourcePlanStatus.Submitted, today, ct);
            int[] contractIds = plans.Select(p => p.ContractId).Distinct().ToArray();
            Dictionary<int, int> managers = contractIds.Length == 0 ? new Dictionary<int, int>()
                : (await ResFacts.ManagerRows(uow, contractIds).ToListAsync(ct)).GroupBy(t => t.ContractId).ToDictionary(g => g.Key, g => g.First().Manager);
            var result = new List<AgentWorkItem>();
            foreach (ResFacts.PlanGap g in plans)
            {
                bool named = managers.TryGetValue(g.ContractId, out int manager) && g.MakerUserId != manager && !PcResourceRules.IsEditor(g.EditorUserIds, manager);
                AgentWorkItem item = ResFacts.PlanItem(g, named ? "طرح منابع ارسال شده است؛ تایید آن فقط با مدیر پروژه همین قرارداد است." : "طرح منابع ارسال شده است؛ مدیر پروژه‌ای که بتواند تصمیم بگیرد نیست و تایید با PMO است.", named ? null : "PMO");
                if (named) item.GateUserId = manager;
                result.Add(item);
            }
            return result;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo
        {
            Roles = item.GateUserId.HasValue ? null : "PMO", UserId = item.GateUserId, Link = ResFacts.PlanLink(item.ContractId ?? 0), Title = "تایید طرح منابع " + item.TargetNumber,
        };
    }

    /// <summary>RES.PLAN.FIX: the project manager returned the plan with a reason; the planner corrects it and submits it again.</summary>
    internal sealed class ResPlanFixStep : ResStep
    {
        public override string Code => "RES.PLAN.FIX";
        public override string Title => "اصلاح و ارسال دوباره طرح منابع برگشت‌خورده";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<ResFacts.PlanGap> gaps = await ResFacts.GapsAsync(uow, PcResourcePlanStatus.Returned, today, ct);
            return gaps.Select(g => ResFacts.PlanItem(g, "مدیر پروژه طرح منابع را برگرداند" + (string.IsNullOrWhiteSpace(g.ReturnReason) ? "." : ": " + g.ReturnReason) + " اصلاح کنید و دوباره ارسال کنید.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.PlanRoles, Link = ResFacts.PlanLink(item.ContractId ?? 0), Title = "اصلاح طرح منابع " + item.TargetNumber };
    }

    internal abstract class ResRateStep : ProcStep
    {
        public override int Station => 4;
        public override string AgentCode => AgentCodes.Plan;
    }

    /// <summary>RES.RATE.INPUT: a resource that a plan uses has no approved standard rate (and is allowed to carry one); the finance manager proposes it. No rate is ever set on a resource linked to a person.</summary>
    internal sealed class ResRateInputStep : ResRateStep
    {
        public override string Code => "RES.RATE.INPUT";
        public override string Title => "ثبت نرخ استاندارد منابع طرح";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            // the rate owner of a used resource: itself, or the role resource of an employee-linked one
            IQueryable<int> used = uow.Set<PcActivityResource>().AsNoTracking().Where(x => !x.Plan.Deleted && !x.Resource.Deleted).Select(x => x.Resource.RateResourceId ?? x.ResourceId).Distinct();
            var rows = await uow.Set<PcResource>().AsNoTracking().Where(r => !r.Deleted && r.EmployeeId == null && r.RateResourceId == null && used.Contains(r.Id)
                    && !uow.Set<PcResourceRate>().Any(x => x.ResourceId == r.Id && !x.Deleted && (x.Status == PcRateStatus.Approved || x.Status == PcRateStatus.Submitted)))
                .OrderBy(r => r.Code).ThenBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.Code, r.Title }).ToListAsync(ct);
            return rows.Select(r => Item("PcResource", r.Id, r.Code, null, null, $"منبع «{r.Title}» در طرح منابع به کار رفته ولی نرخ استاندارد تاییدشده ندارد؛ نرخ را پیشنهاد دهید.", ResFacts.RateMakerRoles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.RateMakerRoles, Link = ResFacts.RatesLink(item.TargetId), Title = "نرخ منبع " + item.TargetNumber };
    }

    /// <summary>RES.RATE.APPROVE (gate): a second finance person approves a proposed rate (never the one who proposed it). The amount is not in the task text.</summary>
    internal sealed class ResRateApproveStep : ResRateStep
    {
        public override string Code => "RES.RATE.APPROVE";
        public override string Title => "تایید نرخ استاندارد منبع توسط نفر دوم مالی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PcResourceRate>().AsNoTracking().Where(x => !x.Deleted && x.Status == PcRateStatus.Submitted && !x.Resource.Deleted)
                .OrderBy(x => x.Id).Take(ProcStepCommon.Cap).Select(x => new { x.Id, x.ResourceId, x.Resource.Code, x.Resource.Title }).ToListAsync(ct);
            return rows.Select(r => Item("PcResourceRate", r.Id, r.Code, null, null, $"نرخ استاندارد منبع «{r.Title}» پیشنهاد و ارسال شده است؛ تایید با نفر دوم مالی است.", ResFacts.RateApproverRoles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.RateApproverRoles, Link = ResFacts.RatesLink(), Title = "تایید نرخ منبع " + item.TargetNumber };
    }
}
