using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
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

        /// <summary>A handover whose package is incomplete RIGHT NOW: a required line is missing, or the platform dossier of its case is not complete (the status read; the service also re-checks the confirmed hash).</summary>
        protected static System.Linq.Expressions.Expression<Func<EnHandover, bool>> Incomplete(IUnitOfWork uow) =>
            h => h.Lines.Any(l => l.Required && l.State == EnLineState.Missing)
                || uow.Set<EnCase>().Any(c => c.Id == h.CaseId && c.Classification == EnClassification.Platform
                    && !uow.Set<EnPlatformDossier>().Any(p => !p.Deleted && p.ProductId == c.ProductId && p.ReferenceCode == c.ReferenceCode && p.Status == EnDossierStatus.Complete));

        /// <summary>The live state of the packages, read by the SAME rule as the service (lines + the platform dossier of the case + the approved permit): the missing count, the unconfirmed dossier and the release lock text (null = the release is possible).</summary>
        protected sealed class PackageState
        {
            public int Missing;
            public bool DossierUnconfirmed;
            public string ReleaseLock;
            public bool Locked => Missing > 0 || DossierUnconfirmed;
        }

        protected static async Task<Dictionary<int, PackageState>> PackageStatesAsync(IUnitOfWork uow, List<EnHandover> handovers, CancellationToken ct)
        {
            Dictionary<int, PackageState> result = new Dictionary<int, PackageState>();
            foreach (EnHandover h in handovers)
            {
                EnCase c = await uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == h.CaseId, ct);
                bool unconfirmed = c != null && c.Classification == EnClassification.Platform && !(c.ProductId.HasValue && await EnPlatformRules.IsCompleteAsync(uow, c.ProductId.Value, c.ReferenceCode, ct));
                int missing = h.Lines.Count(l => l.Required && l.State == EnLineState.Missing);
                EnWaiver approved = await uow.Set<EnWaiver>().AsNoTracking().Where(w => w.HandoverId == h.Id && !w.Deleted && w.Status == EnWaiverStatus.Approved && w.UsedAt == null).OrderBy(w => w.Id).FirstOrDefaultAsync(ct);
                result[h.Id] = new PackageState { Missing = missing, DossierUnconfirmed = unconfirmed, ReleaseLock = EnHandoverRules.ReleaseLock(h.WaiverId.HasValue, missing, unconfirmed, EnHandoverRules.MissingKeys(h.Lines), approved?.MissingSnapshot, approved != null) };
            }
            return result;
        }

        /// <summary>The open debts that can close now: a platform dossier line that is available (or was declared not applicable by an approved deviation: the debt is voided with a note), a system BOM / OPC line whose released BOM / approved routing exists, an approved project MDR row with a file of the same type and title, or the confirmed dossier of a dossier debt.</summary>
        protected static IQueryable<int> ReadyDebtIds(IUnitOfWork uow) =>
            uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => !d.Deleted && d.Status == EnDebtStatus.Open
                && ((d.HandoverLineId == null && d.DocumentType == EnDebtTypes.DossierConfirm
                        && uow.Set<EnCase>().Any(c => c.Id == d.CaseId && uow.Set<EnPlatformDossier>().Any(p => !p.Deleted && p.ProductId == c.ProductId && p.ReferenceCode == c.ReferenceCode && p.Status == EnDossierStatus.Complete)))
                    || (d.HandoverLineId != null && uow.Set<EnHandoverLine>().Any(l => l.Id == d.HandoverLineId
                        && ((l.PlatformDocumentId != null && uow.Set<EnPlatformDocument>().Any(p => p.Id == l.PlatformDocumentId && ((p.State == EnLineState.Available && (p.DocumentId != null || p.SystemProven)) || p.State == EnLineState.NotApplicable)))
                            || (l.PlatformDocumentId == null && l.LinkPath != null
                                && ((l.DocumentType == EnDocTypes.Bom && uow.Set<PbBomHeader>().Any(b => !b.Deleted && b.ProductId == d.ProductId && b.Status == PbBomStatus.Released))
                                    || (l.DocumentType == EnDocTypes.Opc && uow.Set<PbOpcRouting>().Any(o => !o.Deleted && o.ProductId == d.ProductId && o.Status == PbRoutingStatus.Approved)
                                        && !uow.Set<PbOpcRouting>().Any(o => !o.Deleted && o.ProductId == d.ProductId && (o.Status == PbRoutingStatus.Draft || o.Status == PbRoutingStatus.UnderReview)))))
                            || (l.PlatformDocumentId == null && l.LinkPath == null && uow.Set<PcEngineeringDeliverable>().Any(m => !m.Deleted && m.ContractId == d.ContractId && m.DocumentType == l.DocumentType && m.Title == l.Title && m.IssueState == PcIssueState.Approved && m.DocumentId != null)))))))
                .Select(d => d.Id);
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
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.ContractId != null && (c.Classification != EnClassification.None || (c.Kind != EnCaseKind.Turbine && c.EngConfirmedAt != null)) && (c.Status == EnCaseStatus.Awarded || c.Status == EnCaseStatus.InEngineering)
                    && (c.CharterReviewOutcome == EnCharterOutcome.Confirmed || !uow.Set<PcProjectCharter>().Any(ch => ch.ContractId == c.ContractId && !ch.Deleted))
                    && !uow.Set<EnHandover>().Any(h => h.CaseId == c.Id && !h.Deleted && (h.Status == EnHandoverStatus.Draft || h.Status == EnHandoverStatus.Submitted || h.Status == EnHandoverStatus.Accepted || h.Status == EnHandoverStatus.Released)))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId, Returned = uow.Set<EnHandover>().Any(h => h.CaseId == c.Id && h.Status == EnHandoverStatus.Returned) }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, r.Returned ? $"تحویل مدارک پروندهٔ {r.Number} را PMO برگرداند؛ دور تازه باید پیش‌نویس شود." : $"پروندهٔ {r.Number} برنده شده و تحویل مدارک به PMO هنوز پیش‌نویس نشده است.")).ToList();
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, new EnHandoverActionDto { Action = "draft", CaseId = item.TargetId }, "ایجنت مهندسی پیش‌نویس تحویل مدارک {n} را از فهرست MDL و منابع ساخت.", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/cases/" + Id(item.TargetId), Title = "پیش‌نویس تحویل مدارک پروندهٔ " + item.TargetNumber };
    }

    /// <summary>ENG.HANDOVER.COMPLETE (Input): a handover with required documents still missing. In the draft the engineers upload them (an incomplete package may still go to the PMO for the rounds); once the PMO accepted an incomplete package, the manager either completes it or asks the CEO for the emergency permit (asked ONCE, only then).</summary>
    internal sealed class EnHandoverCompleteStep : EnHandoverStep
    {
        public override string Code => "ENG.HANDOVER.COMPLETE";
        public override string Title => "تکمیل مدارک الزامی تحویل یا درخواست مجوز اضطرار پس از پذیرش PMO (مدارک را مهندسی دارد)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<AgentWorkItem> items = new List<AgentWorkItem>();
            // the draft: the engineers upload the missing documents (an incomplete package still goes to the PMO)
            var drafts = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Draft && h.Lines.Any(l => l.Required && l.State == EnLineState.Missing))
                .OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId, Missing = h.Lines.Count(l => l.Required && l.State == EnLineState.Missing) }).ToListAsync(ct);
            items.AddRange(drafts.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null,
                $"تحویل {r.Number}: {r.Missing} مدرک الزامی هنوز موجود نیست؛ بستهٔ ناقص هم برای رفت‌وبرگشت به PMO می‌رود ولی ابلاغ به اجرا فقط با مدارک کامل یا مجوز اضطرار مدیرعامل (پس از پذیرش PMO) ممکن است.")));
            // accepted and still locked, nothing asked yet: the waiting work is the engineering MANAGER's (supply the documents and refresh, confirm the dossier, or ask the permit once)
            List<EnHandover> accepted = await uow.Set<EnHandover>().AsNoTracking().Include(h => h.Lines).Where(h => !h.Deleted && h.Status == EnHandoverStatus.Accepted && h.WaiverId == null
                    && !uow.Set<EnWaiver>().Any(w => w.HandoverId == h.Id && !w.Deleted && (w.Status == EnWaiverStatus.Requested || (w.Status == EnWaiverStatus.Approved && w.UsedAt == null))))
                .OrderBy(h => h.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, PackageState> states = await PackageStatesAsync(uow, accepted, ct);
            foreach (EnHandover h in accepted)
            {
                PackageState st = states[h.Id];
                if (!st.Locked) continue;
                string text = st.Missing > 0
                    ? $"تحویل {h.Number}: PMO محتوای رسیده را پذیرفته ولی {st.Missing} مدرک الزامی هنوز موجود نیست و ابلاغ به اجرا قفل است؛ مدیر مهندسی مدارک رسیده را بازخوانی می‌کند (بسته دوباره برای PMO می‌رود) یا یک‌بار مجوز اضطرار از مدیرعامل می‌خواهد (پس از رد، فقط با دلیل تازه)."
                    : $"تحویل {h.Number}: همهٔ مدارک موجود است و فقط تایید کامل‌بودن پروندهٔ پلتفرم مانده؛ مدیر مهندسی پرونده را تایید کند تا قفل باز شود (برای این مجوز اضطرار لازم نیست).";
                items.Add(Item("EnHandover", h.Id, h.Number, h.ContractId, null, text, EnRoles.Manager));
            }
            return items.Take(ProcStepCommon.Cap).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? EnRoles.Writers, Link = HandoverLink(item.TargetId), Title = "تکمیل مدارک یا درخواست مجوز اضطرار تحویل " + item.TargetNumber };
    }

    /// <summary>ENG.HANDOVER.SUBMIT (HumanGate): the engineering manager sends the draft to the PMO for review, complete or not (the lock sits at the release to execution).</summary>
    internal sealed class EnHandoverSubmitStep : EnHandoverStep
    {
        public override string Code => "ENG.HANDOVER.SUBMIT";
        public override string Title => "ارسال تحویل مدارک به PMO توسط مدیر مهندسی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Draft)
                .OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId, Missing = h.Lines.Count(l => l.Required && l.State == EnLineState.Missing) }).ToListAsync(ct);
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, r.Missing > 0
                ? $"تحویل مدارک {r.Number} ناقص است ({r.Missing} مدرک الزامی کم است) و برای بررسی و رفت‌وبرگشت با PMO آمادهٔ ارسال است؛ از مدیرعامل در این مرحله تاییدی گرفته نمی‌شود."
                : $"تحویل مدارک {r.Number} آمادهٔ ارسال به PMO است.", EnRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = HandoverLink(item.TargetId), Title = "ارسال تحویل مدارک " + item.TargetNumber + " به PMO" };
    }

    /// <summary>ENG.WAIVER.DECIDE (HumanGate): only the CEO decides the emergency waiver of the handover lock.</summary>
    internal sealed class EnWaiverDecideStep : EnHandoverStep
    {
        public override string Code => "ENG.WAIVER.DECIDE";
        public override string Title => "تصمیم مدیرعامل دربارهٔ مجوز اضطرار قفل ابلاغ مدارک به اجرا (پس از پذیرش PMO)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            // only a decidable permit: its handover is Accepted (after the rounds with the PMO) and still incomplete
            IQueryable<int> decidable = uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Accepted).Where(Incomplete(uow)).Select(h => h.Id);
            var rows = await uow.Set<EnWaiver>().AsNoTracking().Where(w => !w.Deleted && w.Status == EnWaiverStatus.Requested && decidable.Contains(w.HandoverId))
                .OrderBy(w => w.Id).Take(ProcStepCommon.Cap).Select(w => new { w.Id, w.Number, ContractId = uow.Set<EnHandover>().Where(h => h.Id == w.HandoverId).Select(h => (int?)h.ContractId).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnWaiver", r.Id, r.Number, r.ContractId, null, $"مجوز اضطرار {r.Number} منتظر تصمیم مدیرعامل است.", EnRoles.Ceo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Ceo, Link = Root + "/waivers/" + Id(item.TargetId), Title = "تصمیم دربارهٔ مجوز اضطرار " + item.TargetNumber };
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
            return rows.Select(r => Item("EnDocumentDebt", r.Id, r.Title, r.ContractId, null, $"مدرک «{r.Title}» که بدهی مجوز اضطرار بود اکنون موجود است (یا بند آن با انحراف تاییدشده غیرقابل‌اجرا شده) و بدهی بسته می‌شود.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<int> r = await services.GetRequiredService<IEnHandoverService>().SettleDebtsAsync(ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            EnDebtStatus status = await uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => d.Id == item.TargetId).Select(d => d.Status).FirstOrDefaultAsync(ct);
            if (status == EnDebtStatus.Void) return AgentStepResult.Done("EnDocumentDebt", item.TargetId, item.TargetNumber, $"ایجنت مهندسی بدهی مدرک «{item.TargetNumber}» را بست: بند آن با انحراف تاییدشدهٔ مدیرعامل غیرقابل‌اجرا شده است.");
            if (status != EnDebtStatus.Settled) return AgentStepResult.Waiting("بدهی هنوز منبع مدرک ندارد.");
            return AgentStepResult.Done("EnDocumentDebt", item.TargetId, item.TargetNumber, $"ایجنت مهندسی بدهی مدرک «{item.TargetNumber}» را از روی مدرک رسیده تسویه کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/debts", Title = "تسویهٔ بدهی مدرک " + item.TargetNumber };
    }

    /// <summary>ENG.DEBT.SUPPLY (Input): an open document debt that has no source yet; the engineers must produce the document (overdue ones are said so).</summary>
    internal sealed class EnDebtSupplyStep : EnHandoverStep
    {
        public override string Code => "ENG.DEBT.SUPPLY";
        public override string Title => "تهیهٔ مدرک بدهی مجوز اضطرار (مدارک را مهندسی دارد)";
        public override int OfficeDeadlineHours => 0;   // the debt has its own deadline (DueAt of the debt)
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> ready = ReadyDebtIds(uow);
            DateTimeOffset day = DateTimeOffset.Now;
            var rows = await uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => !d.Deleted && d.Status == EnDebtStatus.Open && !ready.Contains(d.Id)).OrderBy(d => d.DueAt).ThenBy(d => d.Id).Take(ProcStepCommon.Cap)
                .Select(d => new { d.Id, d.Title, d.ContractId, Overdue = d.DueAt < day }).ToListAsync(ct);
            return rows.Select(r => Item("EnDocumentDebt", r.Id, r.Title, r.ContractId, null, r.Overdue ? $"بدهی مدرک «{r.Title}» از سررسید گذشته و هنوز مدرکی نیامده است." : $"مدرک «{r.Title}» بدهی مجوز اضطرار است و باید تا سررسید تهیه شود.")).ToList();
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
            List<EnHandover> rows = await uow.Set<EnHandover>().AsNoTracking().Include(h => h.Lines).Where(h => !h.Deleted && h.Status == EnHandoverStatus.Submitted).OrderBy(h => h.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, PackageState> states = await PackageStatesAsync(uow, rows, ct);   // live completeness (the dossier is read now), not the value stored at the submit
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, !states[r.Id].Locked || r.WaiverId != null ? $"تحویل مدارک {r.Number} منتظر پذیرش است." : $"تحویل مدارک {r.Number} ناقص برای بررسی آمده و منتظر پذیرش یا برگشت PMO است؛ رفت‌وبرگشت بدون مدیرعامل است و ابلاغ به اجرا پس از پذیرش تا رسیدن مدارک یا مجوز اضطرار مدیرعامل قفل می‌ماند.", EnRoles.PmoSide)).ToList();
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
            // the PMO's gate opens only when the release is POSSIBLE (complete, or an approved covering permit); while it is locked the waiting work belongs to the manager (ENG.HANDOVER.COMPLETE) or the CEO (ENG.WAIVER.DECIDE)
            List<EnHandover> accepted = await uow.Set<EnHandover>().AsNoTracking().Include(h => h.Lines).Where(h => !h.Deleted && h.Status == EnHandoverStatus.Accepted).OrderBy(h => h.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, PackageState> states = await PackageStatesAsync(uow, accepted, ct);
            return accepted.Where(h => states[h.Id].ReleaseLock == null)
                .Select(h => Item("EnHandover", h.Id, h.Number, h.ContractId, null, $"تحویل مدارک {h.Number} پذیرفته شده و ابلاغ به اجرا ممکن است (مدارک کامل است یا مجوز اضطرار تاییدشده دارد)؛ منتظر ابلاغ PMO است.", EnRoles.Pmo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Pmo, Link = HandoverLink(item.TargetId), Title = "ابلاغ مدارک " + item.TargetNumber + " به اجرا" };
    }

    /// <summary>ENG.DIST.DRAFT (Agent): a handover accepted by the PMO has no distribution rows yet; the agent builds one row per unit with a deterministic notice text that lists only what that unit needs. The PMO may edit a text until the release.</summary>
    internal sealed class EnDistDraftStep : EnHandoverStep
    {
        public override string Code => "ENG.DIST.DRAFT";
        public override string Title => "ایجنت آغاز پروژه (سمت PMO): تهیهٔ یادداشت ابلاغ هر واحد برای تحویل پذیرفته‌شده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string AgentCode => AgentCodes.Init;   // the notices to the units go engineering -> PMO -> units: the PMO-side agent seat sends them, never the engineering agent
        public override string FallbackRoles => EnRoles.Pmo;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Accepted && !uow.Set<EnDistribution>().Any(d => d.HandoverId == h.Id && !d.Deleted))
                .OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, $"تحویل {r.Number} پذیرفته شده ولی یادداشت ابلاغ واحدها هنوز ساخته نشده است.")).ToList();
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, new EnHandoverActionDto { Action = "draft-distribution", Id = item.TargetId }, "ایجنت سمت PMO برای تحویل {n} یادداشت ابلاغ هر واحد را ساخت؛ PMO می‌تواند آن‌ها را پیش از ابلاغ ویرایش کند.", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Pmo, Link = HandoverLink(item.TargetId), Title = "یادداشت ابلاغ واحدها برای " + item.TargetNumber };
    }

    /// <summary>ENG.DIST.NOTIFY (Agent): a released handover whose units were not notified yet; the agent sends each unit its own notice text.</summary>
    internal sealed class EnDistNotifyStep : EnHandoverStep
    {
        public override string Code => "ENG.DIST.NOTIFY";
        public override string Title => "ایجنت آغاز پروژه (سمت PMO): اطلاع‌رسانی مدارک ابلاغ‌شده به واحدها";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string AgentCode => AgentCodes.Init;   // the notices to the units go engineering -> PMO -> units: the PMO-side agent seat sends them, never the engineering agent
        public override string FallbackRoles => EnRoles.Pmo;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnHandover>().AsNoTracking().Where(h => !h.Deleted && h.Status == EnHandoverStatus.Released && !uow.Set<EnDistribution>().Any(d => d.HandoverId == h.Id && !d.Deleted && d.NoticeSentAt != null))
                .OrderBy(h => h.Id).Take(ProcStepCommon.Cap).Select(h => new { h.Id, h.Number, h.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnHandover", r.Id, r.Number, r.ContractId, null, $"مدارک {r.Number} ابلاغ شده ولی به واحدها اطلاع داده نشده است.")).ToList();
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, new EnHandoverActionDto { Action = "distribute", Id = item.TargetId }, "ایجنت سمت PMO ابلاغ مدارک {n} را با یادداشت مخصوص هر واحد به واحدهای تضمین کیفیت، کنترل کیفیت، تولید، تدارکات، انبار و برنامه‌ریزی اطلاع داد.", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Pmo, Link = HandoverLink(item.TargetId), Title = "اطلاع‌رسانی مدارک " + item.TargetNumber + " به واحدها" };
    }

    /// <summary>ENG.INFO.ANSWER (Input, PMO): a unit that needs more than its notice asked the PMO; the PMO answers (never with a price). The question waits without expiring.</summary>
    internal sealed class EnInfoAnswerStep : EnHandoverStep
    {
        public override string Code => "ENG.INFO.ANSWER";
        public override string Title => "پاسخ PMO به پرسش واحد دربارهٔ مدارک مهندسی";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Pmo;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnInfoRequest>().AsNoTracking().Where(x => !x.Deleted && x.Status == EnInfoStatus.Open).OrderBy(x => x.Id).Take(ProcStepCommon.Cap).Select(x => new { x.Id, x.Number, x.ContractId, x.Unit }).ToListAsync(ct);
            return rows.Select(r => Item("EnInfoRequest", r.Id, r.Number, r.ContractId, null, (r.Unit == EnUnit.Engineering ? $"مهندسی از PMO پرسیده است ({r.Number}) و منتظر پاسخ PMO است (پاسخ بدون قیمت)." : $"واحد {EnText.Unit(r.Unit)} دربارهٔ مدارک مهندسی پرسیده است ({r.Number}) و منتظر پاسخ PMO است (پاسخ بدون قیمت)."), EnRoles.Pmo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Pmo, Link = Root + "/info-requests/" + Id(item.TargetId), Title = "پاسخ به پرسش " + item.TargetNumber };
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
            var rows = await uow.Set<EnDistribution>().AsNoTracking().Where(d => !d.Deleted && d.AcknowledgedAt == null && d.Status == EnDistStatus.Notified && d.NoticeSentAt != null).OrderBy(d => d.Id).Take(ProcStepCommon.Cap)
                .Select(d => new { d.Id, d.HandoverId, d.ContractId, d.Unit, d.Roles, Number = uow.Set<EnHandover>().Where(h => h.Id == d.HandoverId).Select(h => h.Number).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnDistribution", r.Id, r.Number, r.ContractId, null, $"واحد {EnText.Unit(r.Unit)} هنوز دریافت مدارک {r.Number} را تایید نکرده است.", r.Roles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? "QA,QC,PRODUCTIONMANAGER,SHOPSUPERVISOR,CONSTRUCTION,COMMERCIAL,WAREHOUSE,WHSUPERVISOR,PLANNING", Link = Root + "/handovers", Title = "تایید دریافت مدارک " + item.TargetNumber };
    }
}
