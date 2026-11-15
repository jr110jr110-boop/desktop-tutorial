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
using Entities.Model.NasbNiro.Inventory;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.QualityDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 9 (quality): the ITP is made from the template, submitted and put in use by the quality agent; the client approval, the physical inspections and the signature of the release note are
    // for people; the release note draft (with its lines derived from the production receipts and the serial units of the work order) is made as soon as the service's own checks pass.

    public static class QualitySteps
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddQualitySteps(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            services.AddScoped<IAgentStep, ItpEnsureStep>();
            services.AddScoped<IAgentStep, ItpSubmitStep>();
            services.AddScoped<IAgentStep, ItpApproveStep>();
            services.AddScoped<IAgentStep, ItpUseStep>();
            services.AddScoped<IAgentStep, ItpInspectStep>();
            services.AddScoped<IAgentStep, ReleaseDraftStep>();
            services.AddScoped<IAgentStep, ReleaseSignStep>();
            services.AddScoped<IAgentStep, ReportApproveStep>();
            services.AddScoped<IAgentStep, NcrClassifyStep>();
            services.AddScoped<IAgentStep, NcrApproveStep>();
            services.AddScoped<IAgentStep, NcrExecuteStep>();
            services.AddScoped<IAgentStep, NcrCloseStep>();
            services.AddScoped<IAgentStep, CapaAssignStep>();
            services.AddScoped<IAgentStep, CapaActionStep>();
            services.AddScoped<IAgentStep, CapaImplementStep>();
            services.AddScoped<IAgentStep, CapaEffectivenessStep>();
            services.AddScoped<IAgentStep, CapaCloseStep>();
            return services;
        }
    }

    internal abstract class QcStep : ConStep
    {
        public override int StationNo => 9;
        public override string AgentCode => AgentCodes.Qc;

        protected static string ItpLink(int id) => "/quality/itp/" + id.ToString(CultureInfo.InvariantCulture);

        protected static async Task<Dictionary<int, ContractEntity>> LiveAsync(IUnitOfWork uow, DateTime today, IEnumerable<int> ids, CancellationToken ct)
        {
            int[] list = ids.Distinct().Take(ProcStepCommon.Cap).ToArray();
            return (await ConFacts.Live(uow, today).Where(c => list.Contains(c.Id)).ToListAsync(ct)).ToDictionary(c => c.Id);
        }
    }

    /// <summary>QC.ITP.ENSURE: a work order of a running contract (review accepted) that has no inspection and test plan gets one from the template.</summary>
    internal sealed class ItpEnsureStep : QcStep
    {
        public override string Code => "QC.ITP.ENSURE";
        public override string Title => "ایجنت کیفیت: ساخت برنامه بازرسی و آزمون (ITP) از قالب";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "QC,QA,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> accepted = ConFacts.AcceptedContracts(uow);
            IQueryable<int> live = ConFacts.Live(uow, today).Where(c => accepted.Contains(c.Id)).Select(c => c.Id);
            IQueryable<int> planned = uow.Set<Itp>().AsNoTracking().Where(i => !i.Deleted).Select(i => i.WorkOrderId);
            var rows = await uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted && live.Contains(w.ContractId) && !planned.Contains(w.Id)).OrderBy(w => w.Id).Take(ProcStepCommon.Cap)
                .Select(w => new { w.Id, w.Code, w.ContractId }).ToListAsync(ct);
            return rows.Select(w => new AgentWorkItem { TargetType = "WorkOrder", TargetId = w.Id, TargetNumber = w.Code, ContractId = w.ContractId, WorkOrderId = w.Id, Summary = "دستور کار برنامه بازرسی و آزمون ندارد." }).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<ItpDto> r = await services.GetRequiredService<IItpService>().EnsureItpAsync(new EnsureItpDto { WorkOrderId = item.TargetId }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("Itp", r.Data.Id, r.Data.Number, $"ایجنت برنامه بازرسی و آزمون {r.Data.Number} را برای دستور کار {item.TargetNumber} از قالب ساخت.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/quality/itp?workOrder=" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "ITP دستور کار " + item.TargetNumber };
    }

    internal abstract class ItpStepBase : QcStep
    {
        protected static async Task<List<AgentWorkItem>> ByStatusAsync(IUnitOfWork uow, DateTime today, ItpStatus status, bool ownOnly, string summary, string roles, CancellationToken ct)
        {
            // ownOnly: a plan made by ANY agent seat (the QC seat from the template, the planning seat from the ITP activities of the WBS, the production seat): the service lets any planner submit a draft
            // (QualityActionRules.SubmitProblem has no creator rule), and the client approval is a separate person (the maker/editor check at the gate).
            int[] seats = ownOnly ? await ProcStepCommon.AllSeatUserIdsAsync(uow, ct) : Array.Empty<int>();
            var rows = await uow.Set<Itp>().AsNoTracking().Where(i => !i.Deleted && i.Status == status && (!ownOnly || seats.Contains(i.CreatorUserId)) && i.Activities.Any()).OrderBy(i => i.Id).Take(ProcStepCommon.Cap)
                .Select(i => new { i.Id, i.Number, i.ContractId, i.WorkOrderId }).ToListAsync(ct);
            Dictionary<int, ContractEntity> live = await LiveAsync(uow, today, rows.Select(r => r.ContractId), ct);
            return rows.Where(r => live.ContainsKey(r.ContractId)).Select(r => new AgentWorkItem { TargetType = "Itp", TargetId = r.Id, TargetNumber = r.Number, ContractId = r.ContractId, WorkOrderId = r.WorkOrderId, Summary = summary, GateRoles = roles }).ToList();
        }

        protected static async Task<AgentStepResult> ActAsync(IServiceProvider services, AgentWorkItem item, string action, string text, CancellationToken ct)
        {
            ResponseDto<ItpDto> r = await services.GetRequiredService<IItpService>().ItpActionAsync(new ItpActionDto { Id = item.TargetId, Action = action }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("Itp", item.TargetId, item.TargetNumber, text);
        }
    }

    /// <summary>QC.ITP.SUBMIT: the plan that the agent made (first revision) is sent for the approval of the client.</summary>
    internal sealed class ItpSubmitStep : ItpStepBase
    {
        public override string Code => "QC.ITP.SUBMIT";
        public override string Title => "ایجنت کیفیت: ارسال برنامه بازرسی و آزمون برای تایید کارفرما";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "QC,QA,PMO";

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, ItpStatus.Draft, true, "برنامه بازرسی و آزمون ساخته‌شده توسط ایجنت منتظر ارسال برای کارفرماست.", null, ct);

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            ActAsync(services, item, "submit", $"ایجنت برنامه بازرسی و آزمون {item.TargetNumber} را برای تایید کارفرما ارسال کرد.", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = ItpLink(item.TargetId), Title = "ارسال ITP " + item.TargetNumber };
    }

    /// <summary>QC.ITP.APPROVE (gate): somebody who did not prepare or submit the plan records the approval of the client (letter reference and date typed at the gate).</summary>
    internal sealed class ItpApproveStep : ItpStepBase
    {
        public override string Code => "QC.ITP.APPROVE";
        public override string Title => "ثبت تایید کارفرما بر برنامه بازرسی و آزمون";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, ItpStatus.Submitted, false, "برنامه برای کارفرما ارسال شده؛ پس از رسیدن تایید کارفرما، مرجع نامه و تاریخ را ثبت کنید (تهیه‌کننده و ارسال‌کننده نمی‌تواند).", "QC,QA,PMO", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "QC,QA,PMO", Link = ItpLink(item.TargetId), Title = "تایید کارفرما بر ITP " + item.TargetNumber };
    }

    /// <summary>QC.ITP.USE: the approved plan is put in use (no decision: the approval was the gate).</summary>
    internal sealed class ItpUseStep : ItpStepBase
    {
        public override string Code => "QC.ITP.USE";
        public override string Title => "ایجنت کیفیت: قراردادن برنامه تاییدشده در حال اجرا";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "QC,QA,PMO";

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, ItpStatus.ClientApproved, false, "برنامه تاییدشده هنوز در حال اجرا ثبت نشده است.", null, ct);

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            ActAsync(services, item, "use", $"ایجنت برنامه {item.TargetNumber} را در حال اجرا قرار داد.", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = ItpLink(item.TargetId), Title = "اجرای ITP " + item.TargetNumber };
    }

    /// <summary>QC.ITP.INSPECT (physical): one task per due activity of an approved plan (planned for today or earlier, or already notified); the inspector does it and records it.</summary>
    internal sealed class ItpInspectStep : QcStep
    {
        public override string Code => "QC.ITP.INSPECT";
        public override string Title => "بازرسی یا آزمون فعالیت برنامه بازرسی";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            DateTime day = today.Date;
            var rows = await uow.Set<ItpActivity>().AsNoTracking().Where(a => (a.Itp.Status == ItpStatus.ClientApproved || a.Itp.Status == ItpStatus.InUse) && !a.Itp.Deleted
                    && (a.Status == ItpActivityStatus.Notified || (a.Status == ItpActivityStatus.Planned && a.PlannedDate != null && a.PlannedDate <= day)))
                .OrderBy(a => a.Id).Take(ProcStepCommon.Cap).Select(a => new { a.Id, a.ItemNo, a.Activity, a.ItpId, a.Itp.ContractId, a.Itp.WorkOrderId, a.Itp.Number }).ToListAsync(ct);
            Dictionary<int, ContractEntity> live = await LiveAsync(uow, today, rows.Select(r => r.ContractId), ct);
            return rows.Where(r => live.ContainsKey(r.ContractId)).Select(r => new AgentWorkItem
            {
                TargetType = "ItpActivity", TargetId = r.Id, TargetNumber = r.Number + "/" + (r.ItemNo ?? r.Id.ToString(CultureInfo.InvariantCulture)), ContractId = r.ContractId, WorkOrderId = r.WorkOrderId,
                Summary = "بازرسی یا آزمون «" + r.Activity + "» موعد رسیده است؛ بازرس QC آن را انجام و ثبت کند.", GateRoles = "QC",
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "QC", Link = "/quality/itp?workOrder=" + (item.WorkOrderId ?? 0).ToString(CultureInfo.InvariantCulture), Title = "بازرسی " + item.TargetNumber };
    }

    /// <summary>QC.RELEASE.DRAFT: when the service's own release checks of a work order all pass, the agent drafts the release note with the lines derived from the confirmed production receipts and the serial units of the work order.</summary>
    internal sealed class ReleaseDraftStep : QcStep
    {
        private readonly IServiceProvider _sp;

        public ReleaseDraftStep(IServiceProvider sp)
        {
            _sp = sp;
        }

        public override string Code => "QC.RELEASE.DRAFT";
        public override string Title => "ایجنت کیفیت: پیش‌نویس برگه ترخیص پس از گذراندن همه بررسی‌ها";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "QC,QA,PMO";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> made = uow.Set<InventoryDocument>().AsNoTracking().Where(d => !d.Deleted && d.Status == InventoryDocumentStatus.Confirmed && d.Type == InventoryDocumentType.ProductionReceipt && d.WorkOrderId != null).Select(d => d.WorkOrderId.Value);
            IQueryable<int> noted = uow.Set<ReleaseNote>().AsNoTracking().Where(n => !n.Deleted && n.Status != ReleaseNoteStatus.Cancelled).Select(n => n.WorkOrderId);
            var rows = await uow.Set<WorkOrder>().AsNoTracking().Where(w => !w.Deleted && made.Contains(w.Id) && !noted.Contains(w.Id)).OrderBy(w => w.Id).Take(20).Select(w => new { w.Id, w.Code, w.ContractId }).ToListAsync(ct);
            Dictionary<int, ContractEntity> live = await LiveAsync(uow, today, rows.Select(r => r.ContractId), ct);
            var items = new List<AgentWorkItem>();
            foreach (var w in rows.Where(r => live.ContainsKey(r.ContractId)))
            {
                ResponseDto<ReleaseChecksDto> checks = await _sp.GetRequiredService<IReleaseService>().GetChecksAsync(w.Id, ct);
                if (checks.Success && checks.Data.CanRelease)
                    items.Add(new AgentWorkItem { TargetType = "WorkOrder", TargetId = w.Id, TargetNumber = w.Code, ContractId = w.ContractId, WorkOrderId = w.Id, Summary = "همه بررسی‌های ترخیص گذشته است؛ برگه ترخیص هنوز پیش‌نویس نشده است." });
            }
            return items;
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            int wo = item.TargetId;
            var lots = await uow.Set<InventoryDocumentLine>().AsNoTracking().Where(l => l.Document.WorkOrderId == wo && l.Document.Status == InventoryDocumentStatus.Confirmed && !l.Document.Deleted && l.Document.Type == InventoryDocumentType.ProductionReceipt)
                .Select(l => new { l.ItemId, l.Quantity, l.Item.TrackingMode }).ToListAsync(ct);
            var lines = new List<SaveReleaseNoteLineDto>();
            foreach (var g in lots.GroupBy(l => l.ItemId).OrderBy(g => g.Key))
            {
                if (g.First().TrackingMode == ItemTrackingMode.Serial)
                {
                    List<string> serials = (await uow.Set<SerialUnit>().AsNoTracking().Where(u => !u.Deleted && u.WorkOrderId == wo && u.ItemId == g.Key
                            && u.Status != SerialUnitStatus.Scrapped && u.Status != SerialUnitStatus.ReturnedToSupplier && u.Status != SerialUnitStatus.Converted).OrderBy(u => u.SerialNumber).Select(u => u.SerialNumber).ToListAsync(ct)).ToList();
                    if (serials.Count > 0) lines.Add(new SaveReleaseNoteLineDto { ItemId = g.Key, Quantity = serials.Count, SerialNumbers = serials });
                }
                else lines.Add(new SaveReleaseNoteLineDto { ItemId = g.Key, Quantity = g.Sum(x => x.Quantity) });
            }
            if (lines.Count == 0) return AgentStepResult.Waiting("کالایی برای ترخیص از رسیدهای ساخت دستور کار به دست نیامد.");
            ResponseDto<ReleaseNoteDto> saved = await services.GetRequiredService<IReleaseService>().SaveNoteAsync(new SaveReleaseNoteDto { WorkOrderId = wo, Lines = lines, Note = "پیش‌نویس ایجنت کیفیت از رسیدهای ساخت و سریال‌های دستور کار." }, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("ReleaseNote", saved.Data.Id, saved.Data.Number, $"ایجنت برگه ترخیص {saved.Data.Number} را با {lines.Count} ردیف پیش‌نویس کرد؛ امضا با مدیر QC است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/quality/release-notes", Title = "برگه ترخیص " + item.TargetNumber };
    }

    /// <summary>QC.RELEASE.SIGN (gate): the QC manager releases the note (never the one who drafted or edited it).</summary>
    internal sealed class ReleaseSignStep : QcStep
    {
        public override string Code => "QC.RELEASE.SIGN";
        public override string Title => "امضای برگه ترخیص و گواهی انطباق توسط مدیر QC";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<ReleaseNote>().AsNoTracking().Where(n => !n.Deleted && n.Status == ReleaseNoteStatus.Draft && n.Lines.Any()).OrderBy(n => n.Id).Take(ProcStepCommon.Cap)
                .Select(n => new { n.Id, n.Number, n.ContractId, n.WorkOrderId }).ToListAsync(ct);
            Dictionary<int, ContractEntity> live = await LiveAsync(uow, today, rows.Select(r => r.ContractId), ct);
            return rows.Where(r => live.ContainsKey(r.ContractId)).Select(r => new AgentWorkItem { TargetType = "ReleaseNote", TargetId = r.Id, TargetNumber = r.Number, ContractId = r.ContractId, WorkOrderId = r.WorkOrderId, Summary = "برگه ترخیص آماده امضاست؛ تنظیم‌کننده یا ویرایش‌کننده آن را امضا نمی‌کند.", GateRoles = "QC" }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "QC", Link = "/quality/release-notes/" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "امضای برگه ترخیص " + item.TargetNumber };
    }

    /// <summary>QC.REPORT.APPROVE (gate): a submitted test report is approved by a QC user who is not its examiner or creator (the service refuses the same person). Approving the last required report also finishes the ITP activity.</summary>
    internal sealed class ReportApproveStep : QcStep
    {
        public override string Code => "QC.REPORT.APPROVE";
        public override string Title => "تایید گزارش آزمون توسط بازرس QC دیگر";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<TestReport>().AsNoTracking().Where(r => !r.Deleted && r.Status == TestReportStatus.Submitted).OrderBy(r => r.Id).Take(ProcStepCommon.Cap)
                .Select(r => new { r.Id, r.Number, r.WorkOrderId }).ToListAsync(ct);
            int[] orderIds = rows.Where(r => r.WorkOrderId.HasValue).Select(r => r.WorkOrderId.Value).Distinct().ToArray();
            Dictionary<int, int> contractOf = orderIds.Length == 0 ? new Dictionary<int, int>() : await uow.Set<WorkOrder>().AsNoTracking().Where(w => orderIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.ContractId, ct);
            Dictionary<int, ContractEntity> live = await LiveAsync(uow, today, contractOf.Values, ct);
            var items = new List<AgentWorkItem>();
            foreach (var r in rows)
            {
                int? contract = r.WorkOrderId.HasValue && contractOf.TryGetValue(r.WorkOrderId.Value, out int c) ? c : (int?)null;
                if (contract.HasValue && !live.ContainsKey(contract.Value)) continue;
                items.Add(new AgentWorkItem { TargetType = "TestReport", TargetId = r.Id, TargetNumber = r.Number, ContractId = contract, WorkOrderId = r.WorkOrderId, Summary = "گزارش آزمون ارسال شده و منتظر تایید است؛ آزمونگر و تهیه‌کننده آن را تایید نمی‌کند.", GateRoles = QualityRoles.Inspectors });
            }
            return items;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = QualityRoles.Inspectors, Link = "/quality/reports/" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "تایید گزارش آزمون " + item.TargetNumber };
    }

    /// <summary>The base of the NCR steps: the open nonconformances of the live contracts (a report without a contract, such as an incoming inspection, is always taken).</summary>
    internal abstract class NcrStepBase : QcStep
    {
        protected static string NcrLink(int id) => "/inventory/ncr/" + id.ToString(CultureInfo.InvariantCulture);

        protected static async Task<List<AgentWorkItem>> ByStatusAsync(IUnitOfWork uow, DateTime today, NcrStatus status, bool classifyOnly, bool signedOnly, Func<NonConformance, string> summary, Func<NonConformance, string> roles, CancellationToken ct)
        {
            IQueryable<NonConformance> q = uow.Set<NonConformance>().AsNoTracking().Where(n => !n.Deleted && n.Status == status);
            if (status == NcrStatus.Open)
            {
                if (classifyOnly) q = q.Where(n => n.Severity == NcrSeverity.NotClassified || n.Disposition == NcrDisposition.None);
                else if (signedOnly) q = q.Where(n => n.Severity != NcrSeverity.NotClassified && n.Disposition != NcrDisposition.None);
            }
            List<NonConformance> rows = await q.OrderBy(n => n.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, ContractEntity> live = await LiveAsync(uow, today, rows.Where(n => n.ContractId.HasValue).Select(n => n.ContractId.Value), ct);
            return rows.Where(n => !n.ContractId.HasValue || live.ContainsKey(n.ContractId.Value))
                .Select(n => new AgentWorkItem { TargetType = "NonConformance", TargetId = n.Id, TargetNumber = n.Number, ContractId = n.ContractId, WorkOrderId = n.WorkOrderId, Summary = summary(n), GateRoles = roles(n) }).ToList();
        }
    }

    /// <summary>QC.NCR.CLASSIFY (gate): a nonconformance that is not classified (minor or major) or has no decision (concession, rework, repair, scrap, return) waits for QC; the classification and the disposition are a person's judgement.</summary>
    internal sealed class NcrClassifyStep : NcrStepBase
    {
        public override string Code => "QC.NCR.CLASSIFY";
        public override string Title => "طبقه‌بندی و تصمیم درباره عدم انطباق";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, NcrStatus.Open, true, false, n => "عدم انطباق منتظر طبقه‌بندی (جزئی یا عمده) و تصمیم است.", n => InventoryRoles.Qc, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? InventoryRoles.Qc, Link = NcrLink(item.TargetId), Title = "طبقه‌بندی عدم انطباق " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>QC.NCR.APPROVE (gate): the decision is signed by QC or QA, engineering and manufacturing (a concession or repair also needs the permit of engineering); the roles that have not signed yet are the gate roles.</summary>
    internal sealed class NcrApproveStep : NcrStepBase
    {
        public override string Code => "QC.NCR.APPROVE";
        public override string Title => "تایید تصمیم عدم انطباق (کیفیت، مهندسی، ساخت)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, NcrStatus.Open, false, true, n => "تصمیم عدم انطباق منتظر امضای نقش‌های باقی‌مانده است؛ ثبت‌کننده یا ویرایشگر گزارش امضا نمی‌کند.", MissingRoles, ct);

        // the same roles as the inbox item of the service (NcrGates.SyncAsync)
        private static string MissingRoles(NonConformance n)
        {
            var roles = new List<string>();
            if (!n.QualityApprovedAt.HasValue) roles.Add(InventoryRoles.RoleQualitySigner);
            if (!n.EngineeringApprovedAt.HasValue || ((n.Disposition == NcrDisposition.Concession || n.Disposition == NcrDisposition.Repair) && !n.ConcessionApprovedAt.HasValue)) roles.Add(InventoryRoles.RoleEngineering);
            if (!n.ManufacturingApprovedAt.HasValue) roles.Add(InventoryRoles.RoleManufacturing);
            return ProcStepCommon.Roles(roles.ToArray());
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo
        {
            Roles = item.GateRoles ?? ProcStepCommon.Roles(InventoryRoles.RoleQualitySigner, InventoryRoles.RoleEngineering, InventoryRoles.RoleManufacturing), Link = NcrLink(item.TargetId), Title = "تایید تصمیم عدم انطباق " + item.TargetNumber, HasOfficeInboxItem = true,
        };
    }

    // The gate list of NcrApproveStep drops an NCR whose signatures are all given but whose status did not move (cannot happen: the service sets Approved with the last signature).

    /// <summary>QC.NCR.EXECUTE (physical): the approved decision is carried out: rework or repair on the shop floor, scrap, return to the supplier or the stock transfer of the warehouse. The target warehouse and the evidence are chosen by the person who does the work.</summary>
    internal sealed class NcrExecuteStep : NcrStepBase
    {
        public override string Code => "QC.NCR.EXECUTE";
        public override string Title => "اجرای تصمیم عدم انطباق (دوباره‌کاری، تعمیر، ضایعات، مرجوعی یا انتقال انبار)";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<AgentWorkItem> items = await ByStatusAsync(uow, today, NcrStatus.Approved, false, false,
                n => n.WarehouseId.HasValue ? "تصمیم تاییدشده منتظر اجرا و سند انبار است." : "تصمیم تاییدشده روی خط تولید اجرا می‌شود (دوباره‌کاری، تعمیر، ضایعات یا مرجوعی) و مدرک آن ثبت می‌شود.", n => "PMO,QC,WAREHOUSE", ct);
            int[] ids = items.Select(i => i.TargetId).ToArray();
            if (ids.Length > 0)
            {
                // a stock document that waits for the confirmation of the warehouse: that is the warehouse's turn
                List<int> waiting = await uow.Set<InventoryDocument>().AsNoTracking().Where(d => !d.Deleted && d.Status == InventoryDocumentStatus.Draft && d.NcrId != null).Select(d => d.NcrId.Value).Take(ProcStepCommon.Cap).ToListAsync(ct);
                foreach (AgentWorkItem i in items.Where(i => waiting.Contains(i.TargetId)))
                {
                    i.Summary = "سند انبار تصمیم ساخته شده و منتظر تایید انبار پس از جابه‌جایی کالاست.";
                    i.GateRoles = InventoryRoles.RoleWarehouse;
                }
            }
            return items;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? "PMO,QC,WAREHOUSE", Link = NcrLink(item.TargetId), Title = "اجرای تصمیم عدم انطباق " + item.TargetNumber };
    }

    /// <summary>QC.NCR.CLOSE (gate): the executed report is closed with the effectiveness check (and, for a rework or repair, the accepted re-inspection) by QC or QA who did not raise, edit or carry it out.</summary>
    internal sealed class NcrCloseStep : NcrStepBase
    {
        public override string Code => "QC.NCR.CLOSE";
        public override string Title => "بستن عدم انطباق پس از اجرا و بررسی اثربخشی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByStatusAsync(uow, today, NcrStatus.Executed, false, false, n => "تصمیم اجرا شده؛ علت ریشه‌ای، اقدام اصلاحی و اثربخشی ثبت و گزارش بسته شود (کسی که آن را ثبت، ویرایش یا اجرا کرده نمی‌بندد).", n => InventoryRoles.QcOrQa, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = InventoryRoles.QcOrQa, Link = NcrLink(item.TargetId), Title = "بستن عدم انطباق " + item.TargetNumber };
    }

    /// <summary>The base of the CAPA steps: a CAPA has no contract of its own; the open ones of the company are taken.</summary>
    internal abstract class CapaStepBase : QcStep
    {
        /// <summary>The roles of the CAPA administration of the service (CapaService.CapaStaff).</summary>
        protected const string Staff = "QA,QC,PMO";

        protected static string CapaLink(int id) => "/quality/capa/" + id.ToString(CultureInfo.InvariantCulture);

        protected static async Task<List<AgentWorkItem>> ByQueryAsync(IQueryable<Capa> q, string summary, string roles, CancellationToken ct)
        {
            var rows = await q.OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number }).ToListAsync(ct);
            return rows.Select(c => new AgentWorkItem { TargetType = "Capa", TargetId = c.Id, TargetNumber = c.Number, Summary = summary, GateRoles = roles }).ToList();
        }
    }

    /// <summary>QC.CAPA.ASSIGN (gate): an open CAPA (the service drafts one automatically for a major NCR or an audit finding) waits for QA to name the owner, the root cause and the actions.</summary>
    internal sealed class CapaAssignStep : CapaStepBase
    {
        public override string Code => "QC.CAPA.ASSIGN";
        public override string Title => "تعیین مسئول، ریشه‌یابی و اقدام‌های CAPA";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByQueryAsync(uow.Set<Capa>().AsNoTracking().Where(c => !c.Deleted && c.Status == CapaStatus.Open), "CAPA باز است؛ مسئول، تحلیل علت ریشه‌ای و اقدام‌ها تعیین شود.", Staff, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = Staff, Link = CapaLink(item.TargetId), Title = "تعریف اقدام‌های CAPA " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>QC.CAPA.ACTION (physical): one task per CAPA whose defined actions are not all done; the owners do them and record the evidence (the inbox of each owner already holds an item per action).</summary>
    internal sealed class CapaActionStep : CapaStepBase
    {
        public override string Code => "QC.CAPA.ACTION";
        public override string Title => "انجام اقدام‌های اصلاحی CAPA و ثبت مدرک";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByQueryAsync(uow.Set<Capa>().AsNoTracking().Where(c => !c.Deleted && c.Status == CapaStatus.ActionsDefined && c.Actions.Any(a => a.DoneAt == null)), "اقدام‌های تعریف‌شده‌ی CAPA هنوز کامل انجام نشده است.", Staff, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = Staff, Link = CapaLink(item.TargetId), Title = "اقدام‌های CAPA " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>QC.CAPA.IMPLEMENT: when every action of the CAPA is done the agent records it as implemented (this starts the waiting period of the effectiveness check; no decision).</summary>
    internal sealed class CapaImplementStep : CapaStepBase
    {
        public override string Code => "QC.CAPA.IMPLEMENT";
        public override string Title => "ایجنت کیفیت: ثبت اجرای کامل اقدام‌های CAPA";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => Staff;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByQueryAsync(uow.Set<Capa>().AsNoTracking().Where(c => !c.Deleted && c.Status == CapaStatus.ActionsDefined && c.Actions.Any() && !c.Actions.Any(a => a.DoneAt == null)), "همه اقدام‌های CAPA انجام شده ولی CAPA هنوز اجراشده ثبت نشده است.", null, ct);

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<CapaDto> r = await services.GetRequiredService<ICapaService>().CapaActionAsync(new CapaActionRequestDto { Id = item.TargetId, Action = "implemented" }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("Capa", item.TargetId, item.TargetNumber, $"ایجنت کیفیت CAPA {item.TargetNumber} را اجراشده ثبت کرد؛ بررسی اثربخشی پس از دوره انتظار با QA یا QC است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = CapaLink(item.TargetId), Title = "ثبت اجرای CAPA " + item.TargetNumber };
    }

    /// <summary>QC.CAPA.EFFECTIVENESS (gate): the task appears only once the waiting period after the implementation has passed (the service refuses an earlier check); QA or QC who did not define or own the CAPA judges it.</summary>
    internal sealed class CapaEffectivenessStep : CapaStepBase
    {
        public override string Code => "QC.CAPA.EFFECTIVENESS";
        public override string Title => "بررسی اثربخشی CAPA پس از دوره انتظار";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            Dictionary<string, decimal> settings = await QualitySettingKeys.LoadAsync(uow, ct);
            DateTime cutoff = today.Date.AddDays(-(int)settings[QualitySettingKeys.EffectivenessWaitDays]);
            return await ByQueryAsync(uow.Set<Capa>().AsNoTracking().Where(c => !c.Deleted && c.Status == CapaStatus.Implemented && c.ImplementedAt != null && c.ImplementedAt <= cutoff),
                "دوره انتظار پس از اجرای CAPA گذشته است؛ اثربخشی بر اساس معیار تعریف‌شده بررسی شود (تعریف‌کننده یا مسئول CAPA نمی‌تواند).", QualityRoles.CapaClosers, ct);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = QualityRoles.CapaClosers, Link = CapaLink(item.TargetId), Title = "اثربخشی CAPA " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    /// <summary>QC.CAPA.CLOSE (gate): a CAPA whose effectiveness was confirmed is closed by QA or the QC manager who did not define or own it.</summary>
    internal sealed class CapaCloseStep : CapaStepBase
    {
        public override string Code => "QC.CAPA.CLOSE";
        public override string Title => "بستن CAPA پس از تایید اثربخشی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            ByQueryAsync(uow.Set<Capa>().AsNoTracking().Where(c => !c.Deleted && c.Status == CapaStatus.EffectivenessCheck), "اثربخشی CAPA تایید شده؛ بستن آن با QA یا QC است.", QualityRoles.CapaClosers, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = QualityRoles.CapaClosers, Link = CapaLink(item.TargetId), Title = "بستن CAPA " + item.TargetNumber, HasOfficeInboxItem = true };
    }
}
