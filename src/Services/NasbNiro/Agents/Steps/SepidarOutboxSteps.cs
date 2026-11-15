using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Finance.Sp;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 13 (finance and Sepidar), the transfer of single documents (the page «انتقال به سپیدار»). All finance work is done inside Orchestra under human supervision; only the kinds that the owner enables
    // are prepared, a person approves every item (never the one who made the source document, never an agent seat), and the sender (none, a file drop, later the API) hands the approved item over.
    //   SP.OUTBOX.PREPARE  agent   prepares the items of the enabled kinds from FINAL documents (a changed document replaces its unsent item; a sent one gets a correction item)
    //   SP.OUTBOX.APPROVE  gate    the finance manager approves or rejects (one by one or a selected set)
    //   SP.OUTBOX.SEND     agent   sends the approved items through the configured sender (does nothing, and is no error, while no sender is configured)
    //   SP.OUTBOX.FIX      input   an item that failed its last automatic attempt: the finance manager retries it or cancels it

    public static class SepidarOutboxSteps
    {
        public static IServiceCollection AddSepidarOutboxSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, SpOutboxPrepareStep>();
            services.AddScoped<IAgentStep, SpOutboxApproveStep>();
            services.AddScoped<IAgentStep, SpOutboxSendStep>();
            services.AddScoped<IAgentStep, SpOutboxFixStep>();
            return services;
        }
    }

    internal abstract class SpStep : ProcStep
    {
        public override int Station => 13;
        public override string AgentCode => AgentCodes.Fin;

        public const string TargetType = "SpOutboxItem";
        public const string PageLink = "/fn/sepidar-transfer";

        public static string ItemLink(int itemId) => PageLink + "?item=" + itemId.ToString(CultureInfo.InvariantCulture);

        /// <summary>The text of a task never carries the number or the title of a restricted item (pay data): only the id.</summary>
        public const string RestrictedText = "قلم محرمانه";

        public static string Number(bool restricted, int id, string number) => restricted ? "SP-" + id.ToString(CultureInfo.InvariantCulture) : number;

        public static string Text(bool restricted, string title) => restricted ? RestrictedText : AgentText.Cut(title, 200);
    }

    /// <summary>SP.OUTBOX.PREPARE: the finance agent prepares the items of every enabled kind from the documents in their final state that have no item (or a changed one). Bounded, idempotent, deterministic, no language model.</summary>
    internal sealed class SpOutboxPrepareStep : SpStep
    {
        private readonly IServiceProvider _sp;

        public SpOutboxPrepareStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        public const string StepCode = "SP.OUTBOX.PREPARE";

        /// <summary>A finished run is not followed by another look at the documents before this time.</summary>
        public static readonly TimeSpan Throttle = TimeSpan.FromMinutes(2);

        public override string Code => StepCode;
        public override string Title => "ایجنت مالی: تهیهٔ اقلام انتقال به سپیدار از اسناد نهایی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => SpRoles.Deciders;

        /// <summary>One run id per five minutes: the repeat guard of the reconciler (30 minutes) is keyed by the target, so a later window is not held back by an earlier one.</summary>
        public static int RunId(DateTimeOffset now) => (int)(now.ToUnixTimeSeconds() / 300);

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            // cheap questions first: nothing is enabled and nothing is stuck; the agent still has a task (kept as it is); the agent finished a moment ago
            Dictionary<string, DateTime?> enabled = await SpFacts.EnabledFromAsync(uow, ct);
            bool recover = await SpFacts.StaleSendingToRecover(uow, DateTimeOffset.Now, _sp.GetRequiredService<ISepidarSender>().IsConfigured).AnyAsync(ct);
            if (enabled.Count == 0 && !recover) return new List<AgentWorkItem>();
            var last = await uow.Set<AgentTask>().AsNoTracking().Where(t => !t.Deleted && t.StepCode == Code).OrderByDescending(t => t.Id)
                .Select(t => new { t.State, t.IsFallback, t.TargetId, t.CreateDate, t.ClosedAt }).FirstOrDefaultAsync(ct);
            if (last != null && !last.IsFallback && (last.State == AgentTaskState.Queued || last.State == AgentTaskState.Running || last.State == AgentTaskState.Returned))
                return new List<AgentWorkItem> { Make(last.TargetId) };
            if (last != null && last.State == AgentTaskState.Done && (last.ClosedAt ?? last.CreateDate) > DateTimeOffset.Now - Throttle) return new List<AgentWorkItem>();

            if (recover) return new List<AgentWorkItem> { Make(RunId(DateTimeOffset.Now)) };
            foreach (SpSource source in SpCatalog.All.Where(s => enabled.ContainsKey(s.Code)))
                if (await SpFacts.NeedingWork(uow, source, enabled[source.Code]).AnyAsync(ct) || await SpFacts.NoLongerFinal(uow, source).AnyAsync(ct))
                    return new List<AgentWorkItem> { Make(RunId(DateTimeOffset.Now)) };
            return new List<AgentWorkItem>();
        }

        private static AgentWorkItem Make(int runId) =>
            Item("SpPrepareRun", runId, "SP-" + runId.ToString(CultureInfo.InvariantCulture), null, null, "سندهایی در وضعیت نهایی هستند که قلم انتقال به سپیدار ندارند یا تغییر کرده‌اند.");

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<SpPrepareResult> r = await services.GetRequiredService<ISpOutboxService>().PrepareAsync(ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            SpPrepareResult p = r.Data;
            if (p.Errors > 0 && p.Prepared == 0) return AgentStepResult.Refused("تهیهٔ اقلام انتقال کامل نشد؛ دوباره تلاش می‌شود: " + p.Describe());
            return AgentStepResult.Done(null, null, null, $"ایجنت مالی {p.Prepared.ToString(CultureInfo.InvariantCulture)} قلم انتقال به سپیدار تهیه کرد ({p.Corrections.ToString(CultureInfo.InvariantCulture)} اصلاحیه، {p.Superseded.ToString(CultureInfo.InvariantCulture)} جایگزین‌شده)؛ تایید با مدیر مالی است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SpRoles.Deciders, Link = PageLink, Title = "تهیهٔ اقلام انتقال به سپیدار" };
    }

    /// <summary>SP.OUTBOX.APPROVE (gate): the finance manager approves or rejects an item (a reason is required to reject). The approver is never the maker of the source document and never an agent seat.</summary>
    internal sealed class SpOutboxApproveStep : SpStep
    {
        public override string Code => "SP.OUTBOX.APPROVE";
        public override string Title => "تایید قلم انتقال به سپیدار";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await SpFacts.OfEnabled(uow, SpOutboxStatus.AwaitingApproval).OrderBy(i => i.Id).Take(ProcStepCommon.Cap)
                .Select(i => new { i.Id, i.SourceNumber, i.Title, i.IsCorrection, i.PayrollRestricted }).ToListAsync(ct);
            return rows.Select(i => Item(TargetType, i.Id, Number(i.PayrollRestricted, i.Id, i.SourceNumber), null, null,
                (i.IsCorrection ? "اصلاحیهٔ سندی که قبلاً به سپیدار ارسال شده است منتظر تایید است: " : "قلم انتقال به سپیدار منتظر تایید است: ") + Text(i.PayrollRestricted, i.Title)
                + " (تاییدکننده نباید ایجادکننده یا ویرایشگر سند منبع باشد).", SpRoles.Deciders)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SpRoles.Deciders, Link = ItemLink(item.TargetId), Title = "تایید انتقال به سپیدار " + item.TargetNumber };
    }

    /// <summary>
    /// SP.OUTBOX.SEND: the finance agent sends an approved item (or a failed one whose time has come) through the configured sender and records the attempt. While no sender is configured the step finds nothing to do: the items
    /// wait in the approved state and no run reports an error. One task per item and attempt (the target type carries the attempt number, the target id is the item), so the backoff of the item (5 minutes, 30 minutes, 2 hours, 12 hours) is not held back by the repeat guard.
    /// </summary>
    internal sealed class SpOutboxSendStep : SpStep
    {
        private readonly IServiceProvider _sp;

        public SpOutboxSendStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        public override string Code => "SP.OUTBOX.SEND";
        public override string Title => "ایجنت مالی: ارسال اقلام تاییدشده به سپیدار";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => SpRoles.Deciders;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!_sp.GetRequiredService<ISepidarSender>().IsConfigured) return new List<AgentWorkItem>();
            var rows = await SpFacts.Sendable(uow, DateTimeOffset.Now).OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.SourceNumber, i.AttemptCount, i.Title, i.PayrollRestricted }).ToListAsync(ct);
            return rows.Select(i => Item(SpFacts.SendTargetType(i.AttemptCount), i.Id, Number(i.PayrollRestricted, i.Id, i.SourceNumber), null, null, "قلم تاییدشده آمادهٔ ارسال به سپیدار است: " + Text(i.PayrollRestricted, i.Title))).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            int id = item.TargetId;
            ResponseDto<SpSendOutcome> r = await services.GetRequiredService<ISpOutboxService>().SendAsync(id, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            switch (r.Data)
            {
                case SpSendOutcome.NoSender: return AgentStepResult.Waiting("اتصال به سپیدار تنظیم نشده است؛ قلم در صف می‌ماند.");
                case SpSendOutcome.Sent: return AgentStepResult.Done(TargetType, id, item.TargetNumber, $"ایجنت مالی قلم {item.TargetNumber} را به سپیدار ارسال کرد.");
                case SpSendOutcome.Failed: return AgentStepResult.Done(TargetType, id, item.TargetNumber, $"ارسال قلم {item.TargetNumber} ناموفق بود؛ تلاش بعدی طبق زمان‌بندی ثبت شد.");
                case SpSendOutcome.Superseded: return AgentStepResult.Done(TargetType, id, item.TargetNumber, $"سند منبع قلم {item.TargetNumber} تغییر کرده بود؛ قلم جایگزین شد و ارسال نشد.");
                default: return AgentStepResult.Done(TargetType, id, item.TargetNumber, $"قلم {item.TargetNumber} اکنون ارسال‌شدنی نبود.");
            }
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SpRoles.Deciders, Link = ItemLink(item.TargetId), Title = "ارسال قلم " + item.TargetNumber + " به سپیدار" };
    }

    /// <summary>SP.OUTBOX.FIX (input): an item that failed its last automatic attempt waits for the finance manager: try again (the item goes back to approved) or cancel it (a reason is required).</summary>
    internal sealed class SpOutboxFixStep : SpStep
    {
        public override string Code => "SP.OUTBOX.FIX";
        public override string Title => "قلم ناموفق انتقال به سپیدار: تلاش دوباره یا لغو";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await SpFacts.NeedingFix(uow).OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.SourceNumber, i.Title, i.AttemptCount, i.LastError, i.PayrollRestricted }).ToListAsync(ct);
            return rows.Select(i => Item(TargetType, i.Id, Number(i.PayrollRestricted, i.Id, i.SourceNumber), null, null,
                $"ارسال قلم «{(i.PayrollRestricted ? RestrictedText : AgentText.Cut(i.Title, 150))}» پس از {i.AttemptCount.ToString(CultureInfo.InvariantCulture)} تلاش ناموفق ماند: {AgentText.Cut(i.LastError, 300)} (تلاش دوباره یا لغو با علت).", SpRoles.Deciders)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SpRoles.Deciders, Link = ItemLink(item.TargetId), Title = "قلم ناموفق انتقال به سپیدار " + item.TargetNumber };
    }
}
