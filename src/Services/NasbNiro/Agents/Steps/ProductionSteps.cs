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
using Entities.Model.NasbNiro.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Production;
using ViewModels.NasbNiro.ProductionDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 8 (production). The production agent drafts the production orders of a work order (from the project baseline, else from the make processes), takes their components again from the master and runs
    // the release check; the supervisors (or the production manager when stock is short) release the order; the shop floor does the physical work (one task per released order, never one per operation per day),
    // and the QC inspector decides on every operation that waits for a QC decision. Nothing here needs a typed input: the check names what is missing and the owners of that data fix it where it lives.

    public static class ProductionSteps
    {
        public static IServiceCollection AddProductionSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, MfOrdersDraftStep>();
            services.AddScoped<IAgentStep, MfComponentsStep>();
            services.AddScoped<IAgentStep, MfReleaseCheckStep>();
            services.AddScoped<IAgentStep, MfReleaseStep>();
            services.AddScoped<IAgentStep, MfShopFloorStep>();
            services.AddScoped<IAgentStep, MfQcDecisionStep>();
            return services;
        }
    }

    internal abstract class MfStep : ProcStep
    {
        public override int Station => 8;
        public override string AgentCode => AgentCodes.Mf;

        protected static string OrderLink(int id) => "/production/orders/" + id.ToString(CultureInfo.InvariantCulture);

        protected static IQueryable<int> LiveIds(IUnitOfWork uow, DateTime today) => ConFacts.Live(uow, today).Select(c => c.Id);
    }

    /// <summary>The verdict of the release check of the draft production orders, read for the two steps that depend on it (the check and the gate).</summary>
    internal static class MfReleaseVerdicts
    {
        /// <summary>The most draft orders whose check is computed in one pass (the check is heavy: drawings, ITP, stock); the oldest come first.</summary>
        public const int Cap = 30;

        public sealed class Verdict
        {
            public int Id { get; set; }
            public string Number { get; set; }
            public int ContractId { get; set; }
            public int WorkOrderId { get; set; }
            public ReleaseCheckResultDto Result { get; set; }

            /// <summary>The failed checks that no override can pass, in one Persian line.</summary>
            public string Reasons => string.Join(" | ", Result.Checks.Where(c => !c.Passed && !c.Overridable).Select(c => c.Title + ": " + c.Message));
        }

        public static async Task<List<Verdict>> ReadAsync(IUnitOfWork uow, IServiceProvider sp, DateTime today, CancellationToken ct)
        {
            IQueryable<int> live = ConFacts.Live(uow, today).Select(c => c.Id);
            var rows = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && o.Status == MfOrderStatus.Draft && live.Contains(o.ContractId))
                .OrderBy(o => o.Id).Take(Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId }).ToListAsync(ct);
            var list = new List<Verdict>();
            if (rows.Count == 0) return list;
            IMfProductionService service = sp.GetRequiredService<IMfProductionService>();
            // the check is a read of the agent step itself: the service lets the step through (AgentMaker.Allows) although the coordinator holds no role
            using (AgentStepContext.Enter("MF.RELEASE.CHECK"))
            {
                foreach (var o in rows)
                {
                    ResponseDto<ReleaseCheckResultDto> r = await service.ReleaseCheckAsync(o.Id, ct);
                    if (r.Success) list.Add(new Verdict { Id = o.Id, Number = o.Number, ContractId = o.ContractId, WorkOrderId = o.WorkOrderId, Result = r.Data });
                }
            }
            return list;
        }
    }

    /// <summary>MF.ORDERS.DRAFT: a work order of a running contract that has a current project baseline or make processes and no production order gets its orders drafted (operations from the approved routing, components from the baseline).</summary>
    internal sealed class MfOrdersDraftStep : MfStep
    {
        public override string Code => "MF.ORDERS.DRAFT";
        public override string Title => "ایجنت تولید: پیش‌نویس دستورهای تولید دستور کار";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ProductionRoles.Planners;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> live = LiveIds(uow, today);
            IQueryable<int> baselines = uow.Set<PbProjectBaseline>().AsNoTracking().Where(b => !b.Deleted && b.IsCurrent).Select(b => b.WorkOrderId);
            IQueryable<int> processes = uow.Set<ProcessEntity>().AsNoTracking().Where(p => !p.Deleted && !p.Bom.Deleted
                    && (p.Type == ProcessType.Service || p.Type == ProcessType.Assembly || p.Type == ProcessType.FinalAssembly || p.Type == ProcessType.Construction)).Select(p => p.Bom.Assembly.WorkOrderId);
            IQueryable<int> ordered = uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && o.Status != MfOrderStatus.Cancelled).Select(o => o.WorkOrderId);
            var rows = await uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted && live.Contains(w.ContractId) && (baselines.Contains(w.Id) || processes.Contains(w.Id)) && !ordered.Contains(w.Id))
                .OrderBy(w => w.Id).Take(ProcStepCommon.Cap).Select(w => new { w.Id, w.Code, w.ContractId }).ToListAsync(ct);
            return rows.Select(w => Item("WorkOrder", w.Id, w.Code, w.ContractId, w.Id, "دستور کار مبنای BOM یا فرایند ساخت دارد ولی دستور تولید ندارد.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<DraftOrdersResultDto> r = await services.GetRequiredService<IMfDraftingService>().DraftAsync(new DraftOrdersDto { WorkOrderId = item.TargetId }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            if (r.Data.Drafted == 0) return AgentStepResult.Waiting(r.Data.Notes.Count == 0 ? "دستور تولیدی ساخته نشد." : AgentText.Cut(string.Join(" | ", r.Data.Notes), 900));
            return AgentStepResult.Done("MfProductionOrder", r.Data.OrderIds.FirstOrDefault(), null, $"ایجنت تولید {r.Data.Drafted.ToString(CultureInfo.InvariantCulture)} دستور تولید برای دستور کار {item.TargetNumber} پیش‌نویس کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProductionRoles.Planners, Link = "/production/orders", Title = "پیش‌نویس دستورهای تولید دستور کار " + item.TargetNumber };
    }

    /// <summary>MF.COMPONENTS: a draft order that the system drafted and that still has no components (nobody touched it) takes its components again from the master (the project baseline with its approved deviations).</summary>
    internal sealed class MfComponentsStep : MfStep
    {
        public override string Code => "MF.COMPONENTS";
        public override string Title => "ایجنت تولید: دریافت اقلام مصرفی دستور تولید از مبنای BOM";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ProductionRoles.Planners;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> live = LiveIds(uow, today);
            // once the agent (or a person) has edited the order its EditedByUserId is set, so the order is not found again
            var rows = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && o.Status == MfOrderStatus.Draft && o.AutoDrafted && o.EditedByUserId == null && !o.Components.Any() && live.Contains(o.ContractId))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId }).ToListAsync(ct);
            return rows.Select(o => Item("MfProductionOrder", o.Id, o.Number, o.ContractId, o.WorkOrderId, "دستور تولید پیش‌نویس هنوز اقلام مصرفی ندارد.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<OrderDto> r = await services.GetRequiredService<IMfProductionService>().OrderActionAsync(new OrderActionDto { Id = item.TargetId, Action = "refresh-components" }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            int count = r.Data.Components.Count;
            return AgentStepResult.Done("MfProductionOrder", item.TargetId, item.TargetNumber, count == 0
                ? $"ایجنت تولید اقلام دستور {item.TargetNumber} را از مبنا خواست؛ مبنا قلمی برای آن ندارد (آزمون صدور آن را نشان می‌دهد)."
                : $"ایجنت تولید {count.ToString(CultureInfo.InvariantCulture)} قلم مصرفی دستور {item.TargetNumber} را از مبنای BOM گرفت.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProductionRoles.Planners, Link = OrderLink(item.TargetId), Title = "اقلام مصرفی دستور تولید " + item.TargetNumber };
    }

    /// <summary>
    /// MF.RELEASE.CHECK: a draft order whose release check fails for a reason that cannot be overridden is checked by the agent; the reasons go to the task (Waiting) and the check is repeated until it passes
    /// (then the order is no longer found here and the release gate appears). The service decides what passes; nothing is computed twice.
    /// </summary>
    internal sealed class MfReleaseCheckStep : MfStep
    {
        private readonly IServiceProvider _sp;

        public MfReleaseCheckStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        public override string Code => "MF.RELEASE.CHECK";
        public override string Title => "ایجنت تولید: آزمون صدور دستور تولید";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ProductionRoles.Planners;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<MfReleaseVerdicts.Verdict> all = await MfReleaseVerdicts.ReadAsync(uow, _sp, today, ct);
            return all.Where(v => !v.Result.CanReleaseWithOverride).Select(v => Item("MfProductionOrder", v.Id, v.Number, v.ContractId, v.WorkOrderId, "دستور تولید پیش‌نویس در آزمون صدور مردود است: " + AgentText.Cut(v.Reasons, 600))).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<ReleaseCheckResultDto> r = await services.GetRequiredService<IMfProductionService>().ReleaseCheckAsync(item.TargetId, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            List<ReleaseCheckDto> failed = r.Data.Checks.Where(c => !c.Passed && !c.Overridable).ToList();
            if (failed.Count > 0) return AgentStepResult.Waiting(AgentText.Cut(string.Join(" | ", failed.Select(c => c.Title + ": " + c.Message)), 1800));
            return AgentStepResult.Done("MfProductionOrder", item.TargetId, item.TargetNumber, $"آزمون صدور دستور تولید {item.TargetNumber} گذشت؛ صدور با سرپرست کارگاه است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProductionRoles.Planners, Link = OrderLink(item.TargetId), Title = "آزمون صدور دستور تولید " + item.TargetNumber };
    }

    /// <summary>MF.RELEASE (gate): the order that passed the check is released by the shop supervisor or the production manager (never the one who edited it); an order with a stock shortage only by the production manager, with a reason.</summary>
    internal sealed class MfReleaseStep : MfStep
    {
        private readonly IServiceProvider _sp;

        public MfReleaseStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        public override string Code => "MF.RELEASE";
        public override string Title => "صدور دستور تولید";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<MfReleaseVerdicts.Verdict> all = await MfReleaseVerdicts.ReadAsync(uow, _sp, today, ct);
            return all.Where(v => v.Result.CanReleaseWithOverride).Select(v => Item("MfProductionOrder", v.Id, v.Number, v.ContractId, v.WorkOrderId,
                v.Result.CanRelease ? "آزمون صدور گذشته است؛ صدور با سرپرست کارگاه یا مدیر تولید است (ویرایش‌کننده دستور نمی‌تواند)."
                    : "آزمون صدور جز کمبود موجودی گذشته است؛ صدور با کمبود فقط با مدیر تولید و ذکر دلیل ممکن است.",
                v.Result.CanRelease ? ProductionRoles.Supervisors : ProductionRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? ProductionRoles.Supervisors, Link = OrderLink(item.TargetId), Title = "صدور دستور تولید " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>MF.SHOPFLOOR (physical): ONE task for each released order, "operations in progress on the shop floor"; it closes when the order is no longer released or in progress (completed, closed or cancelled). The operators record each operation at the terminal.</summary>
    internal sealed class MfShopFloorStep : MfStep
    {
        public override string Code => "MF.SHOPFLOOR";
        public override string Title => "اجرای عملیات دستور تولید در کارگاه";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> live = LiveIds(uow, today);
            var rows = await uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && (o.Status == MfOrderStatus.Released || o.Status == MfOrderStatus.InProgress) && live.Contains(o.ContractId))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap).Select(o => new { o.Id, o.Number, o.ContractId, o.WorkOrderId, Total = o.Operations.Count(p => !p.Deleted) }).ToListAsync(ct);
            return rows.Select(o => Item("MfProductionOrder", o.Id, o.Number, o.ContractId, o.WorkOrderId,
                $"دستور تولید صادر شده و {o.Total.ToString(CultureInfo.InvariantCulture)} عملیات آن در کارگاه اجرا می‌شود؛ پایان هر عملیات در ترمینال کارگاه ثبت می‌شود.", ProductionRoles.Terminal)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? ProductionRoles.Terminal, Link = "/production/terminal", Title = "اجرای دستور تولید " + item.TargetNumber };
    }

    /// <summary>MF.QC.DECISION (gate): one task for each operation that waits in the QC hold; the inspector measures and decides (accept, rework, reject) and never on his own work.</summary>
    internal sealed class MfQcDecisionStep : MfStep
    {
        public override string Code => "MF.QC.DECISION";
        public override string Title => "تصمیم بازرس QC درباره عملیات در انتظار بازرسی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> live = LiveIds(uow, today);
            IQueryable<MfProductionOrder> orders = uow.Set<MfProductionOrder>().AsNoTracking().Where(o => !o.Deleted && (o.Status == MfOrderStatus.Released || o.Status == MfOrderStatus.InProgress) && live.Contains(o.ContractId));
            var rows = await (from p in uow.Set<MfOperation>().AsNoTracking()
                              join o in orders on p.OrderId equals o.Id
                              where !p.Deleted && p.Status == MfOperationStatus.QcHold && (p.InspectionRequirement == MfInspectionRequirement.Qc || p.InspectionRequirement == MfInspectionRequirement.ItpPoint)
                              orderby p.Id
                              select new { p.Id, p.Sequence, p.Title, OrderNumber = o.Number, o.ContractId, o.WorkOrderId }).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(p => Item("MfOperation", p.Id, p.OrderNumber + "/" + p.Sequence.ToString(CultureInfo.InvariantCulture), p.ContractId, p.WorkOrderId,
                "عملیات «" + p.Title + "» منتظر بازرسی QC است؛ بازرس اندازه می‌گیرد و قبول، دوباره‌کاری یا رد را ثبت می‌کند.", ProductionRoles.Inspectors)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProductionRoles.Inspectors, Link = "/production/operations/" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "بازرسی عملیات " + item.TargetNumber, HasOfficeInboxItem = true };
    }
}
