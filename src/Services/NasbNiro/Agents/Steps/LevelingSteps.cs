using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.ProjectControl;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 4 (planning), priority 4, phase 4c: the load of the resources over all projects. The planning agent finds the over-allocations (RES.LOAD.CHECK) and prepares a levelling proposal for each (RES.LEVEL.PROPOSE);
    // people decide: the planner applies or rejects the proposal (RES.LEVEL.DECIDE), a second person (PMO) confirms the change (RES.LEVEL.CONFIRM). The AI never applies a schedule change and never accepts an over-allocation.

    /// <summary>What the steps of the levelling read from the state.</summary>
    internal static class LvlFacts
    {
        public const string DecideRoles = ResFacts.PlanRoles;
        public const string ConfirmRoles = "PMO";

        /// <summary>The over-allocation check does not run again for the same state of the plans, the dates and the resources within this time (an open alert is looked at again after it).</summary>
        public static readonly TimeSpan Throttle = TimeSpan.FromMinutes(30);

        /// <summary>The check runs at least once a day (a holiday or a live contract that changed leaves no trace in the stamp).</summary>
        public static readonly TimeSpan Daily = TimeSpan.FromHours(24);

        /// <summary>The agent looks again for a proposal for an over-allocation it found no move for after this time.</summary>
        public static readonly TimeSpan ProposalRetry = TimeSpan.FromHours(24);

        public static string OverloadLink(int id) => "/project-control/resources?tab=overloads&overload=" + id.ToString(CultureInfo.InvariantCulture);
        public static string ProposalLink(int id) => "/project-control/resources?tab=overloads&proposal=" + id.ToString(CultureInfo.InvariantCulture);

        private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static string Num(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        internal sealed class OverloadRow
        {
            public int Id { get; set; }
            public int ResourceId { get; set; }
            public string Code { get; set; }
            public string Title { get; set; }
            public DateTime FromDate { get; set; }
            public DateTime ToDate { get; set; }
            public decimal PeakLoad { get; set; }
            public decimal PeakCapacity { get; set; }
        }

        internal sealed class ProposalRow
        {
            public int Id { get; set; }
            public int ContractId { get; set; }
            public string ActivityCode { get; set; }
            public string Note { get; set; }
            public DateTime CurrentStart { get; set; }
            public DateTime ProposedStart { get; set; }
            public int? ApplierUserId { get; set; }
        }

        /// <summary>The ids of the approved plans (the load of the alerts comes from them).</summary>
        public static IQueryable<int> ApprovedPlanIds(IUnitOfWork uow) =>
            uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted && p.Status == PcResourcePlanStatus.Approved).Select(p => p.Id);

        /// <summary>An approved plan assigns some resource: there is a load to look at.</summary>
        public static Task<bool> HasLoadAsync(IUnitOfWork uow, CancellationToken ct)
        {
            IQueryable<int> approved = ApprovedPlanIds(uow);
            return uow.Set<PcActivityResource>().AsNoTracking().AnyAsync(x => approved.Contains(x.PlanId), ct);
        }

        public static Task<bool> AnyOpenAsync(IUnitOfWork uow, CancellationToken ct) =>
            uow.Set<PcResourceOverload>().AsNoTracking().AnyAsync(o => !o.Deleted && o.Status == PcOverloadStatus.Open, ct);

        /// <summary>The latest change of what the load and a levelling proposal depend on (an approved plan, the activities that carry an assignment of an approved plan, the resources, the calendars, the terms of the contracts: the lock of a baseline).</summary>
        public static async Task<DateTimeOffset> LatestChangeAsync(IUnitOfWork uow, CancellationToken ct)
        {
            IQueryable<int> approved = ApprovedPlanIds(uow);
            DateTimeOffset? plans = await uow.Set<PcResourcePlan>().AsNoTracking().Where(p => !p.Deleted && p.Status == PcResourcePlanStatus.Approved).MaxAsync(p => p.ApprovedAt, ct);
            DateTimeOffset? activities = await uow.Set<PcActivityResource>().AsNoTracking().Where(x => approved.Contains(x.PlanId)).MaxAsync(x => (DateTimeOffset?)x.Activity.ModifyDate, ct);
            DateTimeOffset? resources = await uow.Set<PcResource>().AsNoTracking().MaxAsync(r => (DateTimeOffset?)r.ModifyDate, ct);
            DateTimeOffset? calendars = await uow.Set<PcCalendar>().AsNoTracking().MaxAsync(c => (DateTimeOffset?)c.ModifyDate, ct);
            DateTimeOffset? terms = await uow.Set<PcContractTerms>().AsNoTracking().MaxAsync(t => (DateTimeOffset?)t.ModifyDate, ct);
            // priority 4d: the capacity of a linked machine resource is its work centre's
            DateTimeOffset? centres = await uow.Set<Entities.Model.NasbNiro.Production.MfWorkCenter>().AsNoTracking().Where(w => uow.Set<PcResource>().Any(r => !r.Deleted && r.WorkCenterId == w.Id)).MaxAsync(w => (DateTimeOffset?)w.ModifyDate, ct);
            // and the production load of a linked work centre (only when some resource is linked, so nothing else is read): a work order or an operation that changed moves the check
            DateTimeOffset? production = null;
            if (await uow.Set<PcResource>().AsNoTracking().AnyAsync(r => !r.Deleted && r.IsActive && r.Kind == PcResourceKind.Machine && r.Unit == PcResourceUnit.Hour && r.WorkCenterId != null, ct))
            {
                DateTimeOffset? orders = await uow.Set<Entities.Model.NasbNiro.Production.MfProductionOrder>().AsNoTracking().MaxAsync(o => (DateTimeOffset?)o.ModifyDate, ct);
                DateTimeOffset? operations = await uow.Set<Entities.Model.NasbNiro.Production.MfOperation>().AsNoTracking().MaxAsync(o => (DateTimeOffset?)o.ModifyDate, ct);
                production = new DateTimeOffset?[] { orders, operations }.Max();
            }
            return new[] { plans, activities, resources, calendars, terms, centres, production }.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(DateTimeOffset.FromUnixTimeSeconds(1)).Max();
        }

        /// <summary>The latest change as a number that differs when it moved (the key of the check task).</summary>
        public static async Task<int> StampAsync(IUnitOfWork uow, CancellationToken ct) => (int)((await LatestChangeAsync(uow, ct)).ToUnixTimeSeconds() % 2_000_000_000L);

        public static IQueryable<OverloadRow> OpenOverloads(IUnitOfWork uow) =>
            uow.Set<PcResourceOverload>().AsNoTracking().Where(o => !o.Deleted && o.Status == PcOverloadStatus.Open && !o.Resource.Deleted).OrderBy(o => o.Id).Take(ProcStepCommon.Cap)
                .Select(o => new OverloadRow { Id = o.Id, ResourceId = o.ResourceId, Code = o.Resource.Code, Title = o.Resource.Title, FromDate = o.FromDate, ToDate = o.ToDate, PeakLoad = o.PeakLoad, PeakCapacity = o.PeakCapacity });

        /// <summary>
        /// The open over-allocations that have no live proposal (proposed, or applied and not yet confirmed) and that the agent has not just found no move for. A proposal that only waits for a change request (a locked project)
        /// is not live once something it depends on has moved since it was made: the agent looks again.
        /// </summary>
        public static IQueryable<OverloadRow> NeedingProposal(IUnitOfWork uow, DateTimeOffset retryBefore, DateTimeOffset changedSince) =>
            uow.Set<PcResourceOverload>().AsNoTracking().Where(o => !o.Deleted && o.Status == PcOverloadStatus.Open && !o.Resource.Deleted && (o.NoProposalAt == null || o.NoProposalAt < retryBefore)
                    && !uow.Set<PcLevelingProposal>().Any(p => !p.Deleted && p.OverloadId == o.Id
                        && ((p.Status == PcLevelingStatus.Proposed && !(p.NeedsChangeRequest && p.ModifyDate < changedSince)) || (p.Status == PcLevelingStatus.Applied && p.ConfirmedAt == null))))
                .OrderBy(o => o.Id).Take(ProcStepCommon.Cap)
                .Select(o => new OverloadRow { Id = o.Id, ResourceId = o.ResourceId, Code = o.Resource.Code, Title = o.Resource.Title, FromDate = o.FromDate, ToDate = o.ToDate, PeakLoad = o.PeakLoad, PeakCapacity = o.PeakCapacity });

        /// <summary>Proposals waiting for a decision: «پیشنهادشده» on an open over-allocation, or applied and not yet confirmed.</summary>
        public static IQueryable<ProposalRow> Proposals(IUnitOfWork uow, PcLevelingStatus status) =>
            uow.Set<PcLevelingProposal>().AsNoTracking().Where(p => !p.Deleted && p.Status == status && (status != PcLevelingStatus.Proposed || p.Overload.Status == PcOverloadStatus.Open) && (status != PcLevelingStatus.Applied || p.ConfirmedAt == null))
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap)
                .Select(p => new ProposalRow { Id = p.Id, ContractId = p.ContractId, ActivityCode = p.Activity.Code, Note = p.Note, CurrentStart = p.CurrentStart, ProposedStart = p.ProposedStart, ApplierUserId = p.ApplierUserId });

        public static string OverloadText(OverloadRow o) =>
            $"منبع «{o.Title}» ({o.Code}) از {Iso(o.FromDate)} تا {Iso(o.ToDate)} بیش‌تخصیص دارد: اوج بار {Num(o.PeakLoad)} در برابر ظرفیت {Num(o.PeakCapacity)}.";
    }

    /// <summary>RES.LOAD.CHECK: the planning agent recomputes the load of every resource over the next 180 days from the approved plans of all projects and opens, changes or resolves the over-allocation alerts.</summary>
    internal sealed class ResLoadCheckStep : ResStep
    {
        public override string Code => "RES.LOAD.CHECK";
        public override string Title => "ایجنت برنامه‌ریزی: بررسی بیش‌تخصیص منابع در ۱۸۰ روز آینده از طرح‌های تاییدشده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ResFacts.PlanRoles;

        private static AgentWorkItem RunItem(int stamp) => Item("ResourceLoadRun", stamp, stamp.ToString(CultureInfo.InvariantCulture), null, null, "طرح منابع تاییدشده، تاریخ فعالیت‌ها یا ظرفیت منابع عوض شده است و بار منابع باید دوباره بررسی شود.");

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            // cheap questions first, from the latest task of this very step and a few maxima (no heavy computation here):
            //  - the agent still has a queued task: it computes the load itself when it runs;
            //  - nothing to look at (no approved assignment, no open alert): nothing to do;
            //  - the state the load comes from moved since the last run (the stamp is the key of the task): run now;
            //  - the same state: again only every 30 minutes while an alert is open, and at least once a day.
            var last = await uow.Set<AgentTask>().AsNoTracking().Where(t => !t.Deleted && t.StepCode == Code).OrderByDescending(t => t.Id)
                .Select(t => new { t.State, t.IsFallback, t.TargetId, t.CreateDate, t.ClosedAt }).FirstOrDefaultAsync(ct);
            if (last != null && !last.IsFallback && (last.State == AgentTaskState.Queued || last.State == AgentTaskState.Running || last.State == AgentTaskState.Returned)) return new List<AgentWorkItem> { RunItem(last.TargetId) };
            bool open = await LvlFacts.AnyOpenAsync(uow, ct);
            if (!open && !await LvlFacts.HasLoadAsync(uow, ct)) return new List<AgentWorkItem>();
            int stamp = await LvlFacts.StampAsync(uow, ct);
            if (last != null && last.TargetId == stamp)
            {
                DateTimeOffset since = last.ClosedAt ?? last.CreateDate;
                bool due = since < DateTimeOffset.Now - LvlFacts.Daily || (open && since < DateTimeOffset.Now - LvlFacts.Throttle);
                if (!due) return new List<AgentWorkItem>();
            }
            return new List<AgentWorkItem> { RunItem(stamp) };
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PcLoadCheckResultDto> r = await services.GetRequiredService<IPcResourceLoadService>().CheckOverloadsAsync(ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            PcLoadCheckResultDto d = r.Data;
            return AgentStepResult.Done(null, null, null, $"ایجنت بار منابع را برای ۱۸۰ روز آینده از طرح‌های تاییدشده بررسی کرد: {d.Opened} بیش‌تخصیص تازه، {d.Updated} تغییر، {d.Resolved} رفع‌شده و {d.Lapsed} گذشته بدون اصلاح؛ اکنون {d.Open} باز و {d.Accepted} پذیرفته‌شده است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.PlanRoles, Link = "/project-control/resources?tab=load", Title = "بررسی بیش‌تخصیص منابع" };
    }

    /// <summary>RES.LOAD.RESOLVE: an over-allocation stays open until a change of the plan or the schedule resolves it, or the PMO accepts it on purpose with a reason; planning has the task until then (zero tolerance).</summary>
    internal sealed class ResLoadResolveStep : ResStep
    {
        public override string Code => "RES.LOAD.RESOLVE";
        public override string Title => "رفع بیش‌تخصیص منبع (اصلاح طرح یا برنامه، یا پذیرش با PMO)";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<LvlFacts.OverloadRow> rows = await LvlFacts.OpenOverloads(uow).ToListAsync(ct);
            return rows.Select(o => Item("PcResourceOverload", o.Id, o.Code, null, null, LvlFacts.OverloadText(o) + " طرح منابع یا برنامه را اصلاح کنید؛ نگه‌داشتن آن به‌عمد فقط با پذیرش PMO و ذکر علت است.", ResFacts.PlanRoles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.PlanRoles, Link = LvlFacts.OverloadLink(item.TargetId), Title = "بیش‌تخصیص منبع " + item.TargetNumber };
    }

    /// <summary>RES.LEVEL.PROPOSE: for each open over-allocation without a live proposal the planning agent prepares one deterministic proposal (which activity moves, how many working days, the effect on the critical path). It only proposes.</summary>
    internal sealed class ResLevelProposeStep : ResStep
    {
        public override string Code => "RES.LEVEL.PROPOSE";
        public override string Title => "ایجنت برنامه‌ریزی: پیشنهاد تسطیح برای بیش‌تخصیص منبع";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ResFacts.PlanRoles;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<LvlFacts.OverloadRow> rows = await LvlFacts.NeedingProposal(uow, DateTimeOffset.Now - LvlFacts.ProposalRetry, await LvlFacts.LatestChangeAsync(uow, ct)).ToListAsync(ct);
            return rows.Select(o => Item("PcResourceOverload", o.Id, o.Code, null, null, LvlFacts.OverloadText(o) + " پیشنهاد جابه‌جایی ساخته نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<int> r = await services.GetRequiredService<IPcResourceLoadService>().ProposeLevelingAsync(item.TargetId, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            if (r.Data == 0) return AgentStepResult.Done(null, null, null, $"برای بیش‌تخصیص منبع {item.TargetNumber} فعالیتی که بتوان جابه‌جا کرد پیدا نشد (فعالیت شروع‌شده یا بدون شناوری کافی)؛ برنامه‌ریز باید طرح یا برنامه را اصلاح کند.");
            return AgentStepResult.Done("PcLevelingProposal", r.Data, item.TargetNumber, $"ایجنت برای بیش‌تخصیص منبع {item.TargetNumber} یک پیشنهاد تسطیح ساخت؛ اعمال یا رد آن با برنامه‌ریز است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.PlanRoles, Link = LvlFacts.OverloadLink(item.TargetId), Title = "پیشنهاد تسطیح منبع " + item.TargetNumber };
    }

    /// <summary>RES.LEVEL.DECIDE (gate): the planner applies the proposal (the dates change through the schedule service) or rejects it with a reason.</summary>
    internal sealed class ResLevelDecideStep : ResStep
    {
        public override string Code => "RES.LEVEL.DECIDE";
        public override string Title => "تصمیم برنامه‌ریز درباره پیشنهاد تسطیح منابع (اعمال یا رد)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<LvlFacts.ProposalRow> rows = await LvlFacts.Proposals(uow, PcLevelingStatus.Proposed).ToListAsync(ct);
            return rows.Select(p => Item("PcLevelingProposal", p.Id, p.ActivityCode, p.ContractId, null, p.Note, ResFacts.PlanRoles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ResFacts.PlanRoles, Link = LvlFacts.ProposalLink(item.TargetId), Title = "پیشنهاد تسطیح فعالیت " + item.TargetNumber };
    }

    /// <summary>RES.LEVEL.CONFIRM (gate): a second person (PMO, never the one who applied the change) confirms the applied change; if that person refuses, the change is taken back.</summary>
    internal sealed class ResLevelConfirmStep : ResStep
    {
        public override string Code => "RES.LEVEL.CONFIRM";
        public override string Title => "تایید نفر دوم (PMO) برای جابه‌جایی اعمال‌شده برنامه";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<LvlFacts.ProposalRow> rows = await LvlFacts.Proposals(uow, PcLevelingStatus.Applied).ToListAsync(ct);
            return rows.Select(p => Item("PcLevelingProposal", p.Id, p.ActivityCode, p.ContractId, null,
                $"جابه‌جایی فعالیت {p.ActivityCode} از {p.CurrentStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} به {p.ProposedStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} اعمال شده و منتظر تایید نفر دوم است؛ کسی که آن را اعمال کرده تایید نمی‌کند.", LvlFacts.ConfirmRoles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = LvlFacts.ConfirmRoles, Link = LvlFacts.ProposalLink(item.TargetId), Title = "تایید جابه‌جایی فعالیت " + item.TargetNumber };
    }
}
