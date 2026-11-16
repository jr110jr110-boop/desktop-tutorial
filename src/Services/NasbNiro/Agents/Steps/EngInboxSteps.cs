using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.EngInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.EngInbox;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 5, the engineering inbox (ENG-F1). Engineering documents are not typed in: the engineering unit drops a package (one workbook + its document files) into a folder on the server, the engineering agent reads it,
    // keeps the structured copy and continues the chain through the services of the product bank. People still sign, release, check and approve; they also fix a rejected package and decide on a blocked one.
    //   ENG.INBOX.INGEST  agent  scans the inbox folder (or later the Engineering software): reads, validates, keeps and moves the ready packages
    //   ENG.INBOX.APPLY   agent  creates the product, the BOM drafts and the OPC draft in the product bank through its services and sends them for the signatures; links a project package to its contract and project
    //   ENG.INBOX.FIX     input  a rejected or blocked package: the engineering unit corrects and re-drops it (a newer version closes the task) or dismisses it with a reason

    public static class EngInboxSteps
    {
        public static IServiceCollection AddEngInboxSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, EngInboxIngestStep>();
            services.AddScoped<IAgentStep, EngInboxApplyStep>();
            services.AddScoped<IAgentStep, EngInboxFixStep>();
            return services;
        }
    }

    internal abstract class EngInboxStep : ProcStep
    {
        public override int Station => 5;
        public override string AgentCode => AgentCodes.Eng;
        public const string TargetType = "EngInboxPackage";

        public static string PackageLink(int id) => EngInboxCodes.PageLink + "?package=" + id.ToString(CultureInfo.InvariantCulture);
        public static string Number(int id) => "PKG-" + id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>ENG.INBOX.INGEST: the scan. The discovery is a cheap probe of the scan-state row (the folder itself is listed in the run): a scan that finished a moment ago is not followed by another.</summary>
    internal sealed class EngInboxIngestStep : EngInboxStep
    {
        private readonly IServiceProvider _sp;

        public EngInboxIngestStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        /// <summary>A finished scan is not followed by another before this time.</summary>
        public static readonly TimeSpan Throttle = TimeSpan.FromMinutes(2);

        public override string Code => EngInboxCodes.Ingest;
        public override string Title => "ایجنت مهندسی: دریافت بسته‌های مهندسی از صندوق ورودی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EngInboxRoles.Operators;

        public static int RunId(DateTimeOffset now) => (int)(now.ToUnixTimeSeconds() / 120);

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var last = await uow.Set<AgentTask>().AsNoTracking().Where(t => !t.Deleted && t.StepCode == Code).OrderByDescending(t => t.Id).Select(t => new { t.State, t.IsFallback, t.TargetId }).FirstOrDefaultAsync(ct);
            if (last != null && !last.IsFallback && (last.State == AgentTaskState.Queued || last.State == AgentTaskState.Running || last.State == AgentTaskState.Returned)) return new List<AgentWorkItem> { Make(last.TargetId) };
            DateTimeOffset cutoff = DateTimeOffset.Now - Throttle;
            bool fresh = await uow.Set<EngInboxScanState>().AsNoTracking().AnyAsync(s => !s.Deleted && s.LastScanAt > cutoff, ct);
            // the source is asked last, so the queries above always run (the SQL translation test reaches them with a source that is not configured)
            if (fresh || !_sp.GetRequiredService<IEngineeringSource>().IsConfigured) return new List<AgentWorkItem>();
            return new List<AgentWorkItem> { Make(RunId(DateTimeOffset.Now)) };
        }

        private static AgentWorkItem Make(int runId) => Item("EngInboxScan", runId, "ENG-SCAN-" + runId.ToString(CultureInfo.InvariantCulture), null, null, "صندوق ورودی مهندسی باید برای بستهٔ تازه بررسی شود.");

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<ViewModels.NasbNiro.EngInboxDtos.EngInboxScanResultDto> r = await services.GetRequiredService<IEngInboxService>().ScanAsync(ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            var d = r.Data;
            if (d.NotConfigured) return AgentStepResult.Waiting(d.Note ?? "پوشهٔ صندوق ورودی در دسترس نیست.");
            if (d.Errors > 0 && d.Ingested + d.Rejected + d.Duplicates == 0) return AgentStepResult.Refused("خواندن بسته‌های صندوق ورودی کامل نشد؛ دوباره تلاش می‌شود.");
            return AgentStepResult.Done(null, null, null, $"ایجنت مهندسی صندوق ورودی را بررسی کرد: {d.Ingested.ToString(CultureInfo.InvariantCulture)} بستهٔ تازه، {d.Rejected.ToString(CultureInfo.InvariantCulture)} ردشده، {d.Duplicates.ToString(CultureInfo.InvariantCulture)} تکراری.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngInboxRoles.Operators, Link = EngInboxCodes.PageLink, Title = "بررسی صندوق ورودی مهندسی" };
    }

    /// <summary>ENG.INBOX.APPLY: one task per ingested package (platform packages first) and per blocked package that is due for another try (30 minutes; a person may have added the missing master).</summary>
    internal sealed class EngInboxApplyStep : EngInboxStep
    {
        public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

        public override string Code => EngInboxCodes.Apply;
        public override string Title => "ایجنت مهندسی: ساخت BOM و OPC پیش‌نویس در بانک محصول از بستهٔ صندوق ورودی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EngInboxRoles.Operators;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            DateTimeOffset due = DateTimeOffset.Now - RetryAfter;
            var rows = await uow.Set<EngInboxPackage>().AsNoTracking()
                .Where(p => !p.Deleted && (p.Status == EngInboxStatus.Ingested || (p.Status == EngInboxStatus.ApplyBlocked && (p.LastApplyAt == null || p.LastApplyAt < due))))
                .OrderBy(p => p.Kind).ThenBy(p => p.Id).Take(ProcStepCommon.Cap)
                .Select(p => new { p.Id, p.Kind, p.PlatformCode, p.Standard, p.ContractCode, p.ProjectCode, p.PackageVersion }).ToListAsync(ct);
            return rows.Select(p => Item(TargetType, p.Id, Number(p.Id), null, null,
                (p.Kind == EngInboxKind.Platform ? $"بستهٔ پلتفرم {p.PlatformCode} ({p.Standard}) نسخهٔ {p.PackageVersion.ToString(CultureInfo.InvariantCulture)}" : $"بستهٔ پروژه {p.ContractCode} / {p.ProjectCode} نسخهٔ {p.PackageVersion.ToString(CultureInfo.InvariantCulture)}")
                + " دریافت شده و هنوز به بانک محصول نرفته است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<EngApplyResult> r = await services.GetRequiredService<IEngInboxApplyService>().ApplyAsync(item.TargetId, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            switch (r.Data.Outcome)
            {
                case EngApplyOutcome.Applied: return AgentStepResult.Done(TargetType, item.TargetId, item.TargetNumber, "ایجنت مهندسی بستهٔ " + item.TargetNumber + " را پیش برد: " + AgentText.Cut(r.Data.Message, 400));
                case EngApplyOutcome.Blocked: return AgentStepResult.Done(TargetType, item.TargetId, item.TargetNumber, "بستهٔ " + item.TargetNumber + " متوقف ماند؛ واحد مهندسی باید تصمیم بگیرد: " + AgentText.Cut(r.Data.Message, 400));
                default: return AgentStepResult.Done(TargetType, item.TargetId, item.TargetNumber, "بستهٔ " + item.TargetNumber + " اکنون منتظر ایجنت نبود.");
            }
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngInboxRoles.Operators, Link = PackageLink(item.TargetId), Title = "ساخت BOM و OPC از بستهٔ " + item.TargetNumber };
    }

    /// <summary>
    /// ENG.INBOX.FIX (input): a rejected package (the workbook failed validation) and a blocked one (the product bank or a missing master stopped the agent). A rejected package closes when a newer package of the same key is
    /// ingested (a corrected workbook of the same version also closes it, except a refusal for the same version with other content, which needs a higher version), or when it is dismissed with a reason.
    /// </summary>
    internal sealed class EngInboxFixStep : EngInboxStep
    {
        public override string Code => EngInboxCodes.Fix;
        public override string Title => "بستهٔ ردشده یا متوقف صندوق ورودی مهندسی: اصلاح و ارسال دوباره یا کنار گذاشتن";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<EngInboxPackage> all = uow.Set<EngInboxPackage>().AsNoTracking().Where(p => !p.Deleted);
            var rows = await all.Where(p => p.Status == EngInboxStatus.ApplyBlocked
                    || (p.Status == EngInboxStatus.Rejected
                        && !all.Any(q => q.Id != p.Id && q.Kind == p.Kind && q.PlatformCode == p.PlatformCode && q.Standard == p.Standard && q.ContractCode == p.ContractCode && q.ProjectCode == p.ProjectCode
                            && (q.PackageVersion > p.PackageVersion || (q.PackageVersion == p.PackageVersion && p.RejectCode == null && q.Id > p.Id))
                            && (q.Status == EngInboxStatus.Ingested || q.Status == EngInboxStatus.Applied || q.Status == EngInboxStatus.ApplyBlocked || q.Status == EngInboxStatus.Superseded))))
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap)
                .Select(p => new { p.Id, p.Status, p.Kind, p.PlatformCode, p.Standard, p.ContractCode, p.ProjectCode, p.PackageVersion, p.OriginalPath, p.ApplyBlockReason, p.FindingsJson }).ToListAsync(ct);
            return rows.Select(p =>
            {
                string name = p.PackageVersion > 0
                    ? (p.Kind == EngInboxKind.Platform ? $"پلتفرم {p.PlatformCode} ({p.Standard}) نسخهٔ {p.PackageVersion.ToString(CultureInfo.InvariantCulture)}" : $"پروژه {p.ContractCode} / {p.ProjectCode} نسخهٔ {p.PackageVersion.ToString(CultureInfo.InvariantCulture)}")
                    : "پوشهٔ " + p.OriginalPath;
                string text;
                if (p.Status == EngInboxStatus.ApplyBlocked) text = $"بستهٔ {name} به بانک محصول نرفت: {AgentText.Cut(p.ApplyBlockReason, 300)} (اصلاح و ارسال دوباره، یا کنار گذاشتن با علت).";
                else
                {
                    string first = string.Join(" | ", EngInboxService.FindingsOf(p.FindingsJson).Where(f => f.Severity == "error").Take(3).Select(f => (string.IsNullOrEmpty(f.Sheet) ? "" : f.Sheet + (f.Row > 0 ? " سطر " + f.Row.ToString(CultureInfo.InvariantCulture) : "") + ": ") + f.Message));
                    text = $"بستهٔ {name} رد شد: {AgentText.Cut(first, 400)} (اصلاح و گذاشتن دوباره در صندوق ورودی، یا کنار گذاشتن با علت).";
                }
                return Item(TargetType, p.Id, Number(p.Id), null, null, text, EngInboxRoles.Operators);
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EngInboxRoles.Operators, Link = PackageLink(item.TargetId), Title = "بستهٔ مهندسی " + item.TargetNumber + ": اصلاح یا کنار گذاشتن" };
    }
}
