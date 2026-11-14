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
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.ProductBank;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProductBankDtos;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 4 (planning): the WBS, the ITP activities, the weights, the dictionary of the nodes, the critical path run and the first baseline are made by the planning agent. People decide at the gates:
    // the approval of the WBS nodes (project manager or PMO, never the one who made or edited them), the signatures of the unit managers on the baseline and its approval by the PMO deputy.
    // The one input is the cost budget of the activities when no source in the system gives it.

    public static class PlanningSteps
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddPlanningSteps(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            services.AddScoped<IAgentStep, WbsGenerateStep>();
            services.AddScoped<IAgentStep, ItpActivitiesStep>();
            services.AddScoped<IAgentStep, WeightsStep>();
            services.AddScoped<IAgentStep, WbsDictionaryStep>();
            services.AddScoped<IAgentStep, WbsApproveStep>();
            services.AddScoped<IAgentStep, CpmStep>();
            services.AddScoped<IAgentStep, BudgetInputStep>();
            services.AddScoped<IAgentStep, BaselineCreateStep>();
            services.AddScoped<IAgentStep, BaselineSignStep>();
            services.AddScoped<IAgentStep, BaselineApproveStep>();
            return services;
        }
    }

    internal abstract class PlanStep : ConStep
    {
        public override int StationNo => 4;
        public override string AgentCode => AgentCodes.Plan;

        /// <summary>A contract in the planning phase: the WBS step of the initiation is done and no baseline exists yet.</summary>
        protected static IQueryable<ContractEntity> Open(IUnitOfWork uow, DateTime today)
        {
            IQueryable<int> wbs = ConFacts.StepDone(uow, PcInitiationStep.WbsDrafted);
            IQueryable<int> baselines = uow.Set<PcBaseline>().AsNoTracking().Where(b => !b.Deleted).Select(b => b.ContractId);
            IQueryable<int> inForce = uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.CurrentBaselineId != null).Select(t => t.ContractId);
            return ConFacts.Live(uow, today).Where(c => wbs.Contains(c.Id) && !baselines.Contains(c.Id) && !inForce.Contains(c.Id));
        }

        protected static string WbsLink(int contractId) => "/project-control/wbs?contract=" + contractId.ToString(CultureInfo.InvariantCulture);
        protected static string BaselineLink(int contractId) => "/project-control/baselines?contract=" + contractId.ToString(CultureInfo.InvariantCulture);

        /// <summary>True when the dates saved on the activities are the result of the plan run of today; false when they are old or missing; null when the plan cannot be computed (cycle, no start date).</summary>
        protected static async Task<bool?> PlanIsCurrentAsync(IServiceProvider sp, IUnitOfWork uow, int contractId, CancellationToken ct)
        {
            CpmResult run = await sp.GetRequiredService<IPcScheduleService>().PreviewPlanAsync(contractId, null, ct);
            if (run == null) return null;
            var acts = await uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == contractId && !a.Deleted).Select(a => new { a.Id, a.EarlyStart, a.EarlyFinish }).ToListAsync(ct);
            return acts.Count > 0 && acts.All(a => run.Items.TryGetValue(a.Id, out CpmItem item) && a.EarlyStart?.Date == item.EarlyStart.Date && a.EarlyFinish?.Date == item.EarlyFinish.Date);
        }

        protected static async Task<Dictionary<int, decimal>> BudgetsAsync(IUnitOfWork uow, int[] ids, CancellationToken ct) =>
            (await uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && ids.Contains(a.ContractId)).GroupBy(a => a.ContractId).Select(g => new { g.Key, Sum = g.Sum(a => a.BudgetAtCompletion) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Sum);
    }

    /// <summary>PLAN.WBS.GENERATE: the WBS of the work orders after the BOM was requested, from the product bank template (project BOM baseline) or from the legacy BOM.</summary>
    internal sealed class WbsGenerateStep : PlanStep
    {
        public override string Code => "PLAN.WBS.GENERATE";
        public override string Title => "ایجنت برنامه‌ریزی: تولید WBS و فعالیت‌ها از BOM و فرآیندها";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> pending = ConFacts.StepPending(uow, PcInitiationStep.WbsDrafted);
            IQueryable<int> requested = ConFacts.StepDone(uow, PcInitiationStep.BomRequested);
            IQueryable<int> hasOrders = uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted).Select(w => w.ContractId);
            List<ContractEntity> rows = await ConFacts.Live(uow, today).Where(c => pending.Contains(c.Id) && requested.Contains(c.Id) && hasOrders.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, "BOM درخواست شده و WBS قرارداد هنوز تولید نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            List<int> orders = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.ContractId == item.TargetId && !w.Deleted).OrderBy(w => w.Id).Select(w => w.Id).ToListAsync(ct);
            var baselines = await uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => orders.Contains(b.WorkOrderId) && !b.Deleted && b.IsCurrent).Select(b => new { b.WorkOrderId, b.WbsTemplateId, b.PcProductNodeId }).ToListAsync(ct);
            var notes = new List<string>();
            int made = 0;
            foreach (var b in baselines.Where(x => x.PcProductNodeId == null))
            {
                if (b.WbsTemplateId == null) { notes.Add("برای دستور کار " + b.WorkOrderId + " قالب WBS تاییدشده‌ای در بانک محصول نیست."); continue; }
                ResponseDto<PbBaselineDto> r = await services.GetRequiredService<IPbProductService>().GenerateWbsAsync(new PbGenerateWbsDto { WorkOrderId = b.WorkOrderId }, ct);
                if (!r.Success) return ProcStepCommon.Refusal(r);
                if (string.IsNullOrWhiteSpace(r.Data?.WbsNote)) made++; else notes.Add(r.Data.WbsNote);
            }
            List<int> legacy = orders.Where(o => baselines.All(b => b.WorkOrderId != o)).ToList();
            if (legacy.Count > 0)
            {
                ResponseDto<PcWbsGenerateResultDto> r = await services.GetRequiredService<IPcScheduleService>().GenerateWbsAsync(new PcWbsGenerateDto { ContractId = item.TargetId, WorkOrderIds = legacy }, ct);
                if (!r.Success) return ProcStepCommon.Refusal(r);
                made += legacy.Count;
            }
            bool done = await uow.Set<PcInitiationStepRecord>().AsNoTracking().AnyAsync(s => s.Initiation.ContractId == item.TargetId && s.Step == PcInitiationStep.WbsDrafted && s.DoneAt != null, ct);
            if (!done) return AgentStepResult.Waiting(notes.Count > 0 ? string.Join(" ", notes.Take(3)) : "WBS هنوز ساخته نشده است.");
            return AgentStepResult.Done("Contract", item.TargetId, item.TargetNumber, $"ایجنت WBS قرارداد {item.TargetNumber} را برای {made} دستور کار تولید کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = WbsLink(item.TargetId), Title = "WBS قرارداد " + item.TargetNumber };
    }

    /// <summary>PLAN.ITP.ACTIVITIES: the inspection and test points of an ITP that came after the WBS become activities before the release note.</summary>
    internal sealed class ItpActivitiesStep : PlanStep
    {
        public override string Code => "PLAN.ITP.ACTIVITIES";
        public override string Title => "ایجنت برنامه‌ریزی: افزودن فعالیت‌های بازرسی و آزمون (ITP) به برنامه";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> inForce = uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.CurrentBaselineId != null).Select(t => t.ContractId);
            IQueryable<int> used = uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.ItpActivityId != null).Select(a => a.ItpActivityId.Value);
            IQueryable<int> products = uow.Set<PcWbsNode>().AsNoTracking().Where(n => !n.Deleted && n.NodeType == PcWbsNodeType.Product && n.WorkOrderId != null).Select(n => n.WorkOrderId.Value);
            var rows = await uow.Set<ItpActivity>().AsNoTracking().Where(a => a.GatesRelease && a.Status != ItpActivityStatus.NotApplicable && !a.Itp.Deleted && a.Itp.Status != ItpStatus.Closed
                    && !used.Contains(a.Id) && products.Contains(a.Itp.WorkOrderId) && !inForce.Contains(a.Itp.ContractId))
                .Select(a => new { a.Itp.ContractId, a.Itp.WorkOrderId }).Distinct().OrderBy(x => x.WorkOrderId).Take(ProcStepCommon.Cap).ToListAsync(ct);
            int[] ids = rows.Select(r => r.ContractId).Distinct().ToArray();
            Dictionary<int, ContractEntity> contracts = (await ConFacts.Live(uow, today).Where(c => ids.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
            return rows.Where(r => contracts.ContainsKey(r.ContractId)).Select(r => ConFacts.Item(contracts[r.ContractId], "WorkOrder", r.WorkOrderId, "برنامه بازرسی و آزمون دستور کار نقطه‌هایی دارد که هنوز فعالیت برنامه نشده‌اند.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PcWbsGenerateResultDto> r = await services.GetRequiredService<IPcScheduleService>().GenerateItpActivitiesAsync(new PcItpGenerateDto { ContractId = item.ContractId ?? 0, WorkOrderId = item.TargetId }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("WorkOrder", item.TargetId, item.TargetNumber, $"ایجنت {r.Data.ActivitiesCreated} فعالیت بازرسی و آزمون را به برنامه قرارداد {item.TargetNumber} افزود.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/project-control/activities?contract=" + (item.ContractId ?? 0).ToString(CultureInfo.InvariantCulture), Title = "فعالیت‌های ITP " + item.TargetNumber };
    }

    /// <summary>PLAN.WEIGHTS: the weights of the nodes and activities that have none (by budget, else by duration, else equal).</summary>
    internal sealed class WeightsStep : PlanStep
    {
        public override string Code => "PLAN.WEIGHTS";
        public override string Title => "ایجنت برنامه‌ریزی: محاسبه وزن گره‌ها و فعالیت‌های WBS";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> nodes = uow.Set<PcWbsNode>().AsNoTracking().Where(n => !n.Deleted && n.ParentId != null && n.Weight <= 0m).Select(n => n.ContractId);
            IQueryable<int> acts = uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.Weight <= 0m).Select(a => a.ContractId);
            List<ContractEntity> rows = await Open(uow, today).Where(c => nodes.Contains(c.Id) || acts.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, "وزن بعضی گره‌ها یا فعالیت‌های WBS صفر است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<int> r = await services.GetRequiredService<IPcScheduleService>().AutoWeightsAsync(item.TargetId, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("Contract", item.TargetId, item.TargetNumber, $"ایجنت وزن {r.Data} گره و فعالیت قرارداد {item.TargetNumber} را محاسبه کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = WbsLink(item.TargetId), Title = "وزن‌های WBS " + item.TargetNumber };
    }

    /// <summary>PLAN.WBS.DICTIONARY: the dictionary text of every node (what it is, its phase, its unit, what it contains), which a reviewer reads before approving the node.</summary>
    internal sealed class WbsDictionaryStep : PlanStep
    {
        public const int Batch = 200;

        public override string Code => "PLAN.WBS.DICTIONARY";
        public override string Title => "ایجنت برنامه‌ریزی: نوشتن فرهنگ لغت WBS گره‌ها";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> bare = uow.Set<PcWbsNode>().AsNoTracking().Where(n => !n.Deleted && n.ApprovedAt == null && (n.Dictionary == null || n.Dictionary == "")).Select(n => n.ContractId);
            List<ContractEntity> rows = await Open(uow, today).Where(c => bare.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, "گره‌های WBS فرهنگ لغت ندارند.")).ToList();
        }

        private static string Words(PcWbsNodeType t) =>
            t == PcWbsNodeType.Project ? "پروژه" : t == PcWbsNodeType.Product ? "محصول" : t == PcWbsNodeType.Assembly ? "مجموعه" : t == PcWbsNodeType.Part ? "قطعه" : t == PcWbsNodeType.WorkPackage ? "بسته کاری" : "مرحله";

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            var schedule = services.GetRequiredService<IPcScheduleService>();
            List<PcWbsNode> nodes = await uow.Set<PcWbsNode>().AsNoTracking().Where(n => n.ContractId == item.TargetId && !n.Deleted && n.ApprovedAt == null && (n.Dictionary == null || n.Dictionary == "")).OrderBy(n => n.Id).Take(Batch).ToListAsync(ct);
            Dictionary<int, int> counts = (await uow.Set<PcActivity>().AsNoTracking().Where(a => a.ContractId == item.TargetId && !a.Deleted && a.WbsNodeId != null).GroupBy(a => a.WbsNodeId.Value).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.N);
            int saved = 0;
            foreach (PcWbsNode n in nodes)
            {
                counts.TryGetValue(n.Id, out int own);
                var dto = new PcWbsNodeDto
                {
                    Id = n.Id, ContractId = n.ContractId, ParentId = n.ParentId, Code = n.Code, Title = n.Title, NodeType = n.NodeType, Phase = n.Phase, WorkOrderId = n.WorkOrderId, AssemblyId = n.AssemblyId, BomId = n.BomId,
                    ProcessId = n.ProcessId, Weight = n.Weight, BudgetAmount = n.BudgetAmount, ResponsibleUnit = n.ResponsibleUnit, Sort = n.Sort,
                    Dictionary = $"{Words(n.NodeType)} «{n.Title}» با کد {n.Code}" + (n.Phase.HasValue ? "، مرحله " + n.Phase.Value : "") + (string.IsNullOrWhiteSpace(n.ResponsibleUnit) ? "" : "، واحد مسئول " + n.ResponsibleUnit)
                        + (own > 0 ? $"؛ شامل {own} فعالیت مستقیم" : "؛ شامل گره‌ها و فعالیت‌های زیرمجموعه") + ". خروجی: تکمیل همه زیرمجموعه‌ها مطابق مدارک و ITP قرارداد.",
                };
                ResponseDto<PcWbsNodeDto> r = await schedule.SaveWbsNodeAsync(dto, ct);
                if (!r.Success) return ProcStepCommon.Refusal(r);
                saved++;
            }
            return AgentStepResult.Done("Contract", item.TargetId, item.TargetNumber, $"ایجنت فرهنگ لغت {saved} گره WBS قرارداد {item.TargetNumber} را نوشت.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = WbsLink(item.TargetId), Title = "فرهنگ لغت WBS " + item.TargetNumber };
    }

    /// <summary>PLAN.WBS.APPROVE (gate): the project manager or the PMO deputy approves the nodes of the WBS (never the one who made or edited them: the agent).</summary>
    internal sealed class WbsApproveStep : PlanStep
    {
        public override string Code => "PLAN.WBS.APPROVE";
        public override string Title => "تایید گره‌های WBS";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> bare = uow.Set<PcWbsNode>().AsNoTracking().Where(n => !n.Deleted && n.ApprovedAt == null && (n.Dictionary == null || n.Dictionary == "")).Select(n => n.ContractId);
            var waiting = await uow.Set<PcWbsNode>().AsNoTracking().Where(n => !n.Deleted && n.ApprovedAt == null && n.Dictionary != null && n.Dictionary != "").GroupBy(n => n.ContractId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct);
            int[] ids = waiting.Select(w => w.Id).ToArray();
            IQueryable<int> wbs = ConFacts.StepDone(uow, PcInitiationStep.WbsDrafted);
            List<ContractEntity> rows = await ConFacts.Live(uow, today).Where(c => wbs.Contains(c.Id) && ids.Contains(c.Id) && !bare.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, $"{waiting.First(w => w.Id == c.Id).N} گره WBS منتظر تایید است؛ تایید با کسی است که گره را نساخته و ویرایش نکرده است.", "PROJECTMANAGER,PMO")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "PROJECTMANAGER,PMO", Link = WbsLink(item.TargetId), Title = "تایید WBS " + item.TargetNumber };
    }

    /// <summary>PLAN.CPM: the critical path run whenever the dates of the activities are not the result of the network (a baseline freezes dates).</summary>
    internal sealed class CpmStep : PlanStep
    {
        private readonly IServiceProvider _sp;

        public CpmStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        public override string Code => "PLAN.CPM";
        public override string Title => "ایجنت برنامه‌ریزی: محاسبه مسیر بحرانی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> withActivities = uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted).Select(a => a.ContractId);
            List<ContractEntity> rows = await Open(uow, today).Where(c => withActivities.Contains(c.Id)).OrderBy(c => c.Id).Take(30).ToListAsync(ct);
            var items = new List<AgentWorkItem>();
            foreach (ContractEntity c in rows)
            {
                bool? current = await PlanIsCurrentAsync(_sp, uow, c.Id, ct);
                if (current != true) items.Add(ConFacts.ContractItem(c, current == null ? "برنامه قابل محاسبه نیست (حلقه در شبکه یا نبود تاریخ شروع)." : "تاریخ‌های فعالیت‌ها با شبکه فعلی هم‌خوان نیست."));
            }
            return items;
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PcCpmRunDto> r = await services.GetRequiredService<IPcScheduleService>().RunCpmAsync(new PcCpmRunRequestDto { ContractId = item.TargetId, Forecast = false }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            if (r.Data.HasCycle) return AgentStepResult.Waiting(r.Data.Message + " " + string.Join("، ", (r.Data.CycleCodes ?? new List<string>()).Take(8)));
            return AgentStepResult.Done("Contract", item.TargetId, item.TargetNumber, $"ایجنت مسیر بحرانی قرارداد {item.TargetNumber} را محاسبه کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/project-control/schedule?contract=" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "مسیر بحرانی " + item.TargetNumber };
    }

    /// <summary>PLAN.BUDGET.INPUT (Input): the cost budget of the activities. No source in the system gives it for a product-bank WBS (the legacy process program price fills it for the legacy BOM), so a person enters it.</summary>
    internal sealed class BudgetInputStep : PlanStep
    {
        public override string Code => "PLAN.BUDGET.INPUT";
        public override string Title => "ثبت بودجه فعالیت‌ها برای خط مبنا";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> withActivities = uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted).Select(a => a.ContractId);
            List<ContractEntity> rows = await Open(uow, today).Where(c => withActivities.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, decimal> sums = await BudgetsAsync(uow, rows.Select(c => c.Id).ToArray(), ct);
            return rows.Where(c => !sums.TryGetValue(c.Id, out decimal s) || s <= 0m).Select(c => ConFacts.ContractItem(c, "هیچ فعالیتی بودجه ندارد و در سیستم منبعی برای بودجه هزینه فعالیت‌ها نیست؛ بودجه فعالیت‌ها (برآورد هزینه) را وارد کنید تا ایجنت خط مبنا را بسازد.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "PLANNING,PMO", Link = "/project-control/activities?contract=" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "بودجه فعالیت‌ها " + item.TargetNumber };
    }

    /// <summary>PLAN.BASELINE.CREATE: the first baseline (draft with the signature slots of the responsible units) from the calculated plan.</summary>
    internal sealed class BaselineCreateStep : PlanStep
    {
        private readonly IServiceProvider _sp;

        public BaselineCreateStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        public override string Code => "PLAN.BASELINE.CREATE";
        public override string Title => "ایجنت برنامه‌ریزی: ساخت پیش‌نویس خط مبنای اولیه";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> accepted = ConFacts.AcceptedContracts(uow);
            IQueryable<int> reopened = uow.Set<PcContractReview>().AsNoTracking().Where(r => !r.Deleted && r.ContractId != null && r.Decision == PcReviewDecision.ReviewRequired).Select(r => r.ContractId.Value);
            IQueryable<int> withActivities = uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted).Select(a => a.ContractId);
            IQueryable<int> unweighted = uow.Set<PcActivity>().AsNoTracking().Where(a => !a.Deleted && a.BudgetAtCompletion <= 0m && a.Weight <= 0m).Select(a => a.ContractId);
            List<ContractEntity> rows = await Open(uow, today).Where(c => withActivities.Contains(c.Id) && accepted.Contains(c.Id) && !reopened.Contains(c.Id) && !unweighted.Contains(c.Id)).OrderBy(c => c.Id).Take(30).ToListAsync(ct);
            Dictionary<int, decimal> sums = await BudgetsAsync(uow, rows.Select(c => c.Id).ToArray(), ct);
            var items = new List<AgentWorkItem>();
            foreach (ContractEntity c in rows.Where(c => sums.TryGetValue(c.Id, out decimal s) && s > 0m))
                if (await PlanIsCurrentAsync(_sp, uow, c.Id, ct) == true) items.Add(ConFacts.ContractItem(c, "برنامه محاسبه و بودجه ثبت شده است؛ خط مبنای اولیه هنوز ساخته نشده است."));
            return items;
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PcBaselineDto> r = await services.GetRequiredService<IPcBaselineService>().CreateBaselineAsync(new PcBaselineCreateDto { ContractId = item.TargetId, Reason = "خط مبنای اولیه: ساخته‌شده توسط ایجنت از برنامه محاسبه‌شده" }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("PcBaseline", r.Data.Id, item.TargetNumber, $"ایجنت پیش‌نویس خط مبنای قرارداد {item.TargetNumber} را ساخت؛ منتظر امضای مدیران واحدها و تایید PMO است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = BaselineLink(item.TargetId), Title = "خط مبنای " + item.TargetNumber };
    }

    /// <summary>PLAN.BASELINE.SIGN (gate): every responsible unit signs the plan of its own work (one slot per unit).</summary>
    internal sealed class BaselineSignStep : PlanStep
    {
        public override string Code => "PLAN.BASELINE.SIGN";
        public override string Title => "امضای مدیران واحدها بر خط مبنا";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var slots = await uow.Set<PcBaselineSignature>().AsNoTracking().Where(s => s.SignedAt == null && !s.Baseline.Deleted && s.Baseline.Status == PcBaselineStatus.Draft)
                .Select(s => new { s.Baseline.ContractId, BaselineId = s.BaselineId, s.UnitRole }).ToListAsync(ct);
            int[] ids = slots.Select(s => s.ContractId).Distinct().ToArray();
            Dictionary<int, ContractEntity> contracts = (await ConFacts.Live(uow, today).Where(c => ids.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
            return slots.GroupBy(s => new { s.ContractId, s.BaselineId }).Where(g => contracts.ContainsKey(g.Key.ContractId)).OrderBy(g => g.Key.BaselineId).Take(ProcStepCommon.Cap).Select(g =>
            {
                string roles = ProcStepCommon.Roles(g.Select(s => s.UnitRole).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray());
                return ConFacts.Item(contracts[g.Key.ContractId], "PcBaseline", g.Key.BaselineId, "برنامه‌ی هر واحد را مدیر همان واحد امضا می‌کند؛ امضای باقی‌مانده: " + string.Join("، ", g.Select(s => s.UnitRole).Distinct()) + ".", roles);
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? "PMO", Link = BaselineLink(item.ContractId ?? 0), Title = "امضای خط مبنا " + item.TargetNumber };
    }

    /// <summary>PLAN.BASELINE.APPROVE (gate): the PMO deputy approves the baseline once every unit has signed (never the one who made it or signed it).</summary>
    internal sealed class BaselineApproveStep : PlanStep
    {
        public override string Code => "PLAN.BASELINE.APPROVE";
        public override string Title => "تایید خط مبنا توسط معاون PMO";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PcBaseline>().AsNoTracking().Where(b => !b.Deleted && b.Status == PcBaselineStatus.ManagersSigned).OrderBy(b => b.Id).Take(ProcStepCommon.Cap).Select(b => new { b.Id, b.ContractId }).ToListAsync(ct);
            int[] ids = rows.Select(r => r.ContractId).Distinct().ToArray();
            Dictionary<int, ContractEntity> contracts = (await ConFacts.Live(uow, today).Where(c => ids.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
            return rows.Where(r => contracts.ContainsKey(r.ContractId)).Select(r => ConFacts.Item(contracts[r.ContractId], "PcBaseline", r.Id, "همه واحدها امضا کرده‌اند؛ خط مبنا منتظر تایید معاون PMO است.", "PMO")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "PMO", Link = BaselineLink(item.ContractId ?? 0), Title = "تایید خط مبنا " + item.TargetNumber };
    }
}
