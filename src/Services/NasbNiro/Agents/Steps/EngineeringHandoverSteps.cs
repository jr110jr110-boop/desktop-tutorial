using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // ENG-E1: the handover of the engineering documents to the PMO, the CEO emergency waiver, the document debts and the distribution (station 5). The agent drafts, settles debts from the sources and notifies; people supply
    // documents and decide at the gates.

    internal abstract class EnHandoverStep : EnStep
    {
        protected static string HandoverLink(int id) => Root + "/handovers/" + Id(id);

        protected static async Task<AgentStepResult> CallAsync(IServiceProvider services, EnHandoverActionDto dto, string doneSummary, CancellationToken ct)
        {
            ResponseDto<EnHandoverDto> r = await services.GetRequiredService<IEnHandoverService>().HandoverActionAsync(dto, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("EnHandover", r.Data.Id, r.Data.Number, doneSummary.Replace("{n}", r.Data.Number));
        }

        /// <summary>The open debts whose source now exists: a platform dossier line that is available, or an approved project MDR row with a file of the same type and title.</summary>
        protected static IQueryable<int> ReadyDebtIds(IUnitOfWork uow) =>
            from d in uow.Set<EnDocumentDebt>().AsNoTracking()
            where !d.Deleted && d.Status == EnDebtStatus.Open
            join l in uow.Set<EnHandoverLine>().AsNoTracking() on d.HandoverLineId equals l.Id
            where (l.PlatformDocumentId != null && uow.Set<EnPlatformDocument>().Any(p => p.Id == l.PlatformDocumentId && p.State == EnLineState.Available && (p.DocumentId != null || p.SystemProven)))
               || (l.PlatformDocumentId == null && uow.Set<PcEngineeringDeliverable>().Any(m => !m.Deleted && m.ContractId == d.ContractId && m.DocumentType == l.DocumentType && m.Title == l.Title && m.IssueState == PcIssueState.Approved && m.DocumentId != null))
            select d.Id;
    }

    /// <summary>ENG.HANDOVER.DRAFT (Agent): a won case whose charter review is confirmed (or that has no charter) and that has no open handover; the agent drafts the handover from the reference list and the sources, a round after a PMO return included.</summary>
    internal sealed class EnHandoverDraftStep : EnHandoverStep
    {
        public override string Code => "ENG.HANDOVER.DRAFT";
        public override string Title => "ایجنت مهندسی: پیش‌نویس تحویل مدارک به PMO";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.ContractId != null && c.Classification != EnClassification.None && (c.Status == EnCaseStatus.Awarded || c.Status == EnCaseStatus.InEngineering)
                    && (c.CharterReviewOutcome == EnCharterOutcome.Confirmed || !uow.Set<PcProjectCharter>().Any(ch => ch.ContractId == c.ContractId && !ch.Deleted))
                    && !uow.Set<EnHandover>().Any(h => h.CaseId == c.Id && !h.Deleted && (h.Status == EnHandoverStatus.Draft || h.Status == EnHandoverStatus.Submitted || h.Status == EnHandoverStatus.Accepted || h.Status == EnHandoverStatus.Released)))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId, Returned = uow.Set<EnHandover>().Any(h => h.CaseId == c.Id && h.Status == EnHandoverStatus.Returned) }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, r.Returned ? $"تحویل مدارک پروندهٔ {r.Number} را PMO برگرداند؛ دور تازه باید پیش‌نویس شود." : $"پروندهٔ {r.Number} برنده شده و تحویل مدارک به PMO هنوز پیش‌نویس نشده است.")).ToList();
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, new EnHandoverActionDto { Action = "draft", CaseId = item.TargetId }, "ایجنت مهندسی پیش‌نویس تحویل مدارک {n} را از فهرست مرجع و منابع ساخت.", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/cases/" + Id(item.TargetId), Title = "پیش‌نویس تحویل مدارک پروندهٔ " + item.TargetNumber };
    }

    /// <summary>ENG.HANDOVER.COMPLETE (Input): a draft handover with required documents still missing; the engineers upload them (or request an emergency waiver through the manager).</summary>
    internal sealed class EnHandoverCompleteStep : EnHandoverStep
    {
        public override string Code => "ENG.HANDOVER.COMPLETE";
        public override string Title => "تکمیل مدارک الزامی تحویل (مدارک را مهندسی دارد)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Draft && h.Lines.Any(l => l.Required && l.State == EnLineState.Missing))
                .OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId, Missing = h.Lines.Count(l => l.Required && l.State == EnLineState.Missing) }).ToListAsync(ct);
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, $"تحویل {r.Number}: {r.Missing} مدرک الزامی هنوز موجود نیست و ارسال به PMO قفل است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = HandoverLink(item.TargetId), Title = "تکمیل مدارک تحویل " + item.TargetNumber };
    }

    /// <summary>ENG.HANDOVER.SUBMIT (HumanGate): the engineering manager sends a complete draft (or one covered by an approved waiver) to the PMO.</summary>
    internal sealed class EnHandoverSubmitStep : EnHandoverStep
    {
        public override string Code => "ENG.HANDOVER.SUBMIT";
        public override string Title => "ارسال تحویل مدارک به PMO توسط مدیر مهندسی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            var rows = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Draft
                    && (!h.Lines.Any(l => l.Required && l.State == EnLineState.Missing) || uow.Set<EnWaiver>().Any(w => w.HandoverId == h.Id && !w.Deleted && w.Status == EnWaiverStatus.Approved && w.UsedAt == null && w.ExpiresAt > now)))
                .OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, $"تحویل مدارک {r.Number} آمادهٔ ارسال به PMO است.", EnRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = HandoverLink(item.TargetId), Title = "ارسال تحویل مدارک " + item.TargetNumber + " به PMO" };
    }

    /// <summary>ENG.WAIVER.DECIDE (HumanGate): only the CEO decides the emergency waiver of the handover lock.</summary>
    internal sealed class EnWaiverDecideStep : EnHandoverStep
    {
        public override string Code => "ENG.WAIVER.DECIDE";
        public override string Title => "تصمیم مدیرعامل دربارهٔ معافیت اضطراری قفل تحویل مدارک";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            var rows = await uow.Set<EnWaiver>().AsNoTracking().Where(w => !w.Deleted && w.Status == EnWaiverStatus.Requested && w.ExpiresAt > now)
                .OrderBy(w => w.Id).Take(ProcStepCommon.Cap).Select(w => new { w.Id, w.Number, ContractId = uow.Set<EnHandover>().Where(h => h.Id == w.HandoverId).Select(h => (int?)h.ContractId).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnWaiver", r.Id, r.Number, r.ContractId, null, $"معافیت اضطراری {r.Number} منتظر تصمیم مدیرعامل است.", EnRoles.Ceo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Ceo, Link = Root + "/waivers/" + Id(item.TargetId), Title = "تصمیم دربارهٔ معافیت اضطراری " + item.TargetNumber };
    }

    /// <summary>ENG.DEBT.SETTLE (Agent): an open document debt whose source document now exists; the agent settles it from the source (zero tolerance: nothing is settled without the document).</summary>
    internal sealed class EnDebtSettleStep : EnHandoverStep
    {
        public override string Code => "ENG.DEBT.SETTLE";
        public override string Title => "ایجنت مهندسی: تسویهٔ بدهی مدارک از روی مدرک رسیده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> ready = ReadyDebtIds(uow);
            var rows = await uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => ready.Contains(d.Id)).OrderBy(d => d.Id).Take(ProcStepCommon.Cap).Select(d => new { d.Id, d.Title, d.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnDocumentDebt", r.Id, r.Title, r.ContractId, null, $"مدرک «{r.Title}» که بدهی معافیت بود اکنون موجود است و بدهی قابل تسویه است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<int> r = await services.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            EnDebtStatus status = await uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => d.Id == item.TargetId).Select(d => d.Status).FirstOrDefaultAsync(ct);
            if (status != EnDebtStatus.Settled) return AgentStepResult.Waiting("بدهی هنوز منبع مدرک ندارد.");
            return AgentStepResult.Done("EnDocumentDebt", item.TargetId, item.TargetNumber, $"ایجنت مهندسی بدهی مدرک «{item.TargetNumber}» را از روی مدرک رسیده تسویه کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/debts", Title = "تسویهٔ بدهی مدرک " + item.TargetNumber };
    }

    /// <summary>ENG.DEBT.SUPPLY (Input): an open document debt that has no source yet; the engineers must produce the document (overdue ones are said so).</summary>
    internal sealed class EnDebtSupplyStep : EnHandoverStep
    {
        public override string Code => "ENG.DEBT.SUPPLY";
        public override string Title => "تهیهٔ مدرک بدهی معافیت (مدارک را مهندسی دارد)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> ready = ReadyDebtIds(uow);
            DateTime day = today.Date;
            var rows = await uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => !d.Deleted && d.Status == EnDebtStatus.Open && !ready.Contains(d.Id)).OrderBy(d => d.DueDate).ThenBy(d => d.Id).Take(ProcStepCommon.Cap)
                .Select(d => new { d.Id, d.Title, d.ContractId, Overdue = d.DueDate < day }).ToListAsync(ct);
            return rows.Select(r => Item("EnDocumentDebt", r.Id, r.Title, r.ContractId, null, r.Overdue ? $"بدهی مدرک «{r.Title}» از سررسید گذشته و هنوز مدرکی نیامده است." : $"مدرک «{r.Title}» بدهی معافیت است و باید تا سررسید تهیه شود.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/debts", Title = "تهیهٔ مدرک بدهی " + item.TargetNumber };
    }

    /// <summary>ENG.HANDOVER.ACCEPT (HumanGate): the PMO or the project manager of the contract accepts the submitted handover (or returns it with a reason).</summary>
    internal sealed class EnHandoverAcceptStep : EnHandoverStep
    {
        public override string Code => "ENG.HANDOVER.ACCEPT";
        public override string Title => "پذیرش تحویل مدارک مهندسی توسط PMO یا مدیر پروژه";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Submitted).OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId, h.Complete }).ToListAsync(ct);
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, r.Complete ? $"تحویل مدارک {r.Number} منتظر پذیرش است." : $"تحویل مدارک {r.Number} با معافیت اضطراری و بدهی مدارک منتظر پذیرش است.", EnRoles.PmoSide)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.PmoSide, Link = HandoverLink(item.TargetId), Title = "پذیرش تحویل مدارک " + item.TargetNumber };
    }

    /// <summary>ENG.HANDOVER.RELEASE (HumanGate): the PMO releases the accepted package to execution and the units.</summary>
    internal sealed class EnHandoverReleaseStep : EnHandoverStep
    {
        public override string Code => "ENG.HANDOVER.RELEASE";
        public override string Title => "ابلاغ مدارک مهندسی به تولید و واحدها توسط PMO";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Accepted).OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, $"تحویل مدارک {r.Number} پذیرفته شده و منتظر ابلاغ به اجرا است.", EnRoles.Pmo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Pmo, Link = HandoverLink(item.TargetId), Title = "ابلاغ مدارک " + item.TargetNumber + " به اجرا" };
    }

    /// <summary>ENG.DIST.NOTIFY (Agent): a released handover that was not distributed yet; the agent notifies the six units.</summary>
    internal sealed class EnDistNotifyStep : EnHandoverStep
    {
        public override string Code => "ENG.DIST.NOTIFY";
        public override string Title => "ایجنت مهندسی: اطلاع‌رسانی مدارک ابلاغ‌شده به واحدها";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Released && !uow.Set<EnDistribution>().Any(d => d.HandoverId == h.Id && !d.Deleted))
                .OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, $"مدارک {r.Number} ابلاغ شده ولی به واحدها اطلاع داده نشده است.")).ToList();
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, new EnHandoverActionDto { Action = "distribute", Id = item.TargetId }, "ایجنت مهندسی ابلاغ مدارک {n} را به واحدهای کیفیت، بازرسی، تولید، خرید، انبار و برنامه‌ریزی اطلاع داد.", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = HandoverLink(item.TargetId), Title = "اطلاع‌رسانی مدارک " + item.TargetNumber + " به واحدها" };
    }

    /// <summary>ENG.DIST.ACK (Input): a unit confirms the receipt of the released documents (the roles of the row decide who).</summary>
    internal sealed class EnDistAckStep : EnHandoverStep
    {
        public override string Code => "ENG.DIST.ACK";
        public override string Title => "تایید دریافت مدارک مهندسی توسط واحد";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnDistribution>().AsNoTracking().Where(d => !d.Deleted && d.AcknowledgedAt == null).OrderBy(d => d.Id).Take(ProcStepCommon.Cap)
                .Select(d => new { d.Id, d.HandoverId, d.ContractId, d.Unit, d.Roles, Number = uow.Set<EnHandover>().Where(h => h.Id == d.HandoverId).Select(h => h.Number).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnDistribution", r.Id, r.Number, r.ContractId, null, $"واحد {EnText.Unit(r.Unit)} هنوز دریافت مدارک {r.Number} را تایید نکرده است.", r.Roles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? "QA,QC,PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION,COMMERCIAL,WAREHOUSE,WHSUPERVISOR,PLANNING", Link = Root + "/handovers", Title = "تایید دریافت مدارک " + item.TargetNumber };
    }
}
