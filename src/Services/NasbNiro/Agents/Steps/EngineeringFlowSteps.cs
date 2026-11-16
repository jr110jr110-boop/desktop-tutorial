using System;
using System.Collections.Generic;
using System.Globalization;
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
    // ENG-E1 (engineering module, external workflow). The agent of engineering does the process (drafting the reference list, assessing the platforms, opening and proposing the case, drafting the tender package and the handover, settling debts, notifying
    // the units); people confirm at gates, supply the engineering documents and give data the system does not have. Every step calls the services only; the services apply every rule to the agent like to anyone else.

    public static class EngineeringFlowSteps
    {
        public static IServiceCollection AddEngineeringFlowSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, EnRefDraftStep>();
            services.AddScoped<IAgentStep, EnRefInputStep>();
            services.AddScoped<IAgentStep, EnRefApproveStep>();
            services.AddScoped<IAgentStep, EnPlatformAssessStep>();
            services.AddScoped<IAgentStep, EnPlatformCompleteStep>();
            services.AddScoped<IAgentStep, EnPlatformConfirmStep>();
            services.AddScoped<IAgentStep, EnCaseOpenStep>();
            services.AddScoped<IAgentStep, EnCaseProposeStep>();
            services.AddScoped<IAgentStep, EnCaseClassifyInputStep>();
            services.AddScoped<IAgentStep, EnCaseConfirmStep>();
            services.AddScoped<IAgentStep, EnCaseApproveStep>();
            services.AddScoped<IAgentStep, EnTenderDraftStep>();
            services.AddScoped<IAgentStep, EnTenderInputStep>();
            services.AddScoped<IAgentStep, EnTenderRevalidateStep>();
            services.AddScoped<IAgentStep, EnTenderReleaseStep>();
            services.AddScoped<IAgentStep, EnCaseAwardStep>();
            services.AddScoped<IAgentStep, EnCaseCloseStep>();
            services.AddScoped<IAgentStep, EnClarSendStep>();
            services.AddScoped<IAgentStep, EnClarAnswerStep>();
            services.AddScoped<IAgentStep, EnClarCloseStep>();
            services.AddScoped<IAgentStep, EnCharterReviewStep>();
            services.AddScoped<IAgentStep, EnHandoverDraftStep>();
            services.AddScoped<IAgentStep, EnHandoverCompleteStep>();
            services.AddScoped<IAgentStep, EnHandoverSubmitStep>();
            services.AddScoped<IAgentStep, EnWaiverDecideStep>();
            services.AddScoped<IAgentStep, EnDebtSettleStep>();
            services.AddScoped<IAgentStep, EnDebtSupplyStep>();
            services.AddScoped<IAgentStep, EnHandoverAcceptStep>();
            services.AddScoped<IAgentStep, EnHandoverReleaseStep>();
            services.AddScoped<IAgentStep, EnDistNotifyStep>();
            services.AddScoped<IAgentStep, EnDistAckStep>();
            return services;
        }
    }

    internal abstract class EnStep : ProcStep
    {
        public override int Station => 5;
        public override string AgentCode => AgentCodes.Eng;

        protected const string Root = "/engineering";
        protected static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------------------------------ the reference list

    /// <summary>ENG.REF.DRAFT (Agent): there is no reference list at all; the agent drafts one from the active MDR template (the company's template, API 611/612) so that people only correct and approve it.</summary>
    internal sealed class EnRefDraftStep : EnStep
    {
        public override string Code => "ENG.REF.DRAFT";
        public override string Title => "ایجنت مهندسی: پیش‌نویس فهرست مدارک مرجع از الگوی MDR";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            bool any = await uow.Set<EnReferenceList>().AsNoTracking().AnyAsync(l => !l.Deleted && (l.Status == EnRefStatus.Draft || l.Status == EnRefStatus.UnderReview || l.Status == EnRefStatus.Approved), ct);
            if (any) return new List<AgentWorkItem>();
            var t = await uow.Set<PcMdrTemplate>().AsNoTracking().Where(x => !x.Deleted && x.IsActive && x.Lines.Any()).OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Title }).FirstOrDefaultAsync(ct);
            return t == null ? new List<AgentWorkItem>() : new List<AgentWorkItem> { Item("PcMdrTemplate", t.Id, t.Code, null, null, "فهرست مدارک مرجع وجود ندارد؛ الگوی MDR «" + t.Title + "» آمادهٔ تبدیل به پیش‌نویس است.") };
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            PcMdrTemplate t = await uow.Set<PcMdrTemplate>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == item.TargetId && !x.Deleted, ct);
            if (t == null || t.Lines.Count == 0) return AgentStepResult.Waiting("الگوی MDR پیدا نشد یا ردیفی ندارد.");
            EnSaveReferenceDto dto = new EnSaveReferenceDto
            {
                Code = EnText2.Cut("VDDR-" + t.Code, 60), Title = EnText2.Cut("فهرست مدارک مرجع — " + t.Title, 300), StandardSource = EnText2.Cut(t.StandardSource, 200),
                Note = "پیش‌نویس خودکار از الگوی MDR؛ مدارک لازم در مرحلهٔ مناقصه را مهندسی علامت بزند.",
                Lines = t.Lines.OrderBy(l => l.Seq).Select(l => new EnReferenceLineDto { DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, Required = l.Required, ClientSubmittal = l.ClientSubmittal, StandardClause = EnText2.Cut(l.StandardSource, 300) }).ToList(),
            };
            ResponseDto<EnReferenceListDto> response = await services.GetRequiredService<IEnReferenceService>().SaveListAsync(dto, ct);
            if (!response.Success) return ProcStepCommon.Refusal(response);
            return AgentStepResult.Done("EnReferenceList", response.Data.Id, response.Data.Code, $"ایجنت مهندسی پیش‌نویس فهرست مدارک مرجع ({response.Data.LineCount} ردیف) را از الگوی MDR ساخت؛ مهندسی آن را بررسی و ارسال می‌کند.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/reference", Title = "تهیهٔ فهرست مدارک مرجع" };
    }

    /// <summary>ENG.REF.INPUT (Input): a draft list that nobody has sent for approval (the engineers correct it, mark the tender stage documents and send it).</summary>
    internal sealed class EnRefInputStep : EnStep
    {
        public override string Code => "ENG.REF.INPUT";
        public override string Title => "بازبینی و ارسال فهرست مدارک مرجع (محتوای مهندسی)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            var rows = await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Draft).OrderBy(l => l.Id).Take(ProcStepCommon.Cap).Select(l => new { l.Id, l.Code, l.Revision, Lines = l.Lines.Count }).ToListAsync(ct);
            return rows.Select(r => Item("EnReferenceList", r.Id, r.Code, null, null, $"فهرست مدارک مرجع {r.Code} (بازنگری {r.Revision}، {r.Lines} ردیف) پیش‌نویس است و برای تایید ارسال نشده است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/reference/" + Id(item.TargetId), Title = "بازبینی و ارسال فهرست مدارک مرجع " + item.TargetNumber };
    }

    /// <summary>ENG.REF.APPROVE (HumanGate): the engineering manager or the CEO, who did not touch the list, approves it.</summary>
    internal sealed class EnRefApproveStep : EnStep
    {
        public override string Code => "ENG.REF.APPROVE";
        public override string Title => "تایید فهرست مدارک مرجع";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            var rows = await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.UnderReview).OrderBy(l => l.Id).Take(ProcStepCommon.Cap).Select(l => new { l.Id, l.Code, l.Revision }).ToListAsync(ct);
            return rows.Select(r => Item("EnReferenceList", r.Id, r.Code, null, null, $"فهرست مدارک مرجع {r.Code} (بازنگری {r.Revision}) منتظر تایید است.", EnRoles.RefApprovers)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.RefApprovers, Link = Root + "/reference/" + Id(item.TargetId), Title = "تایید فهرست مدارک مرجع " + item.TargetNumber };
    }

    // ------------------------------------------------------------------------------------------ the platform dossiers

    internal static class EnCurrentList
    {
        public static async Task<int?> IdAsync(IUnitOfWork uow, CancellationToken ct) =>
            await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved).OrderByDescending(l => l.ApprovedAt).ThenByDescending(l => l.Id).Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>ENG.PLATFORM.ASSESS (Agent): an active product that has no dossier, or whose dossier is against an older reference list. The agent builds the lines and proves what the system knows (a released BOM, an approved routing).</summary>
    internal sealed class EnPlatformAssessStep : EnStep
    {
        public override string Code => "ENG.PLATFORM.ASSESS";
        public override string Title => "ایجنت مهندسی: ارزیابی مدارک پلتفرم نسبت به فهرست مرجع";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            int? current = await EnCurrentList.IdAsync(uow, ct);
            if (!current.HasValue) return new List<AgentWorkItem>();
            int list = current.Value;
            var rows = await uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.IsActive
                    && !uow.Set<EnPlatformDossier>().Any(d => d.ProductId == p.Id && !d.Deleted && d.Status != EnDossierStatus.NotAssessed && d.ReferenceListId == list))
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Code }).ToListAsync(ct);
            return rows.Select(r => Item("PbProduct", r.Id, r.Code, null, null, $"پلتفرم {r.Code} نسبت به فهرست مدارک مرجع جاری ارزیابی نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<EnDossierDto> response = await services.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = item.TargetId, Action = "assess" }, ct);
            if (!response.Success) return ProcStepCommon.Refusal(response);
            return AgentStepResult.Done("EnPlatformDossier", response.Data.Id, item.TargetNumber, $"ایجنت مهندسی پلتفرم {item.TargetNumber} را ارزیابی کرد: {response.Data.MissingCount} مدرک الزامی کم است از {response.Data.RequiredCount}.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/platforms/" + Id(item.TargetId), Title = "ارزیابی مدارک پلتفرم " + item.TargetNumber };
    }

    /// <summary>ENG.PLATFORM.COMPLETE (Input): an assessed dossier with missing documents (or complete but not yet declared complete): the engineers upload the documents, which exist only in their hands.</summary>
    internal sealed class EnPlatformCompleteStep : EnStep
    {
        public override string Code => "ENG.PLATFORM.COMPLETE";
        public override string Title => "تکمیل مدارک مهندسی پلتفرم (مدارک را مهندسی دارد)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            int? current = await EnCurrentList.IdAsync(uow, ct);
            if (!current.HasValue) return new List<AgentWorkItem>();
            int list = current.Value;
            var rows = await uow.Set<EnPlatformDossier>().AsNoTracking().Where(d => !d.Deleted && d.Status == EnDossierStatus.Incomplete && d.ReferenceListId == list && uow.Set<PbProduct>().Any(p => p.Id == d.ProductId && p.IsActive && !p.Deleted))
                .OrderBy(d => d.Id).Take(ProcStepCommon.Cap).Select(d => new { d.Id, d.ProductId, Code = uow.Set<PbProduct>().Where(p => p.Id == d.ProductId).Select(p => p.Code).FirstOrDefault(), Missing = d.Documents.Count(x => x.Required && x.State == EnLineState.Missing) }).ToListAsync(ct);
            return rows.Select(r => Item("EnPlatformDossier", r.Id, r.Code, null, null, r.Missing > 0 ? $"پلتفرم {r.Code}: {r.Missing} مدرک الزامی هنوز موجود نیست." : $"پلتفرم {r.Code}: همهٔ مدارک الزامی هست؛ مهندسی «کامل است» را ثبت کند.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/platforms", Title = "تکمیل مدارک پلتفرم " + item.TargetNumber };
    }

    /// <summary>ENG.PLATFORM.CONFIRM (HumanGate): a manager who did not supply a document confirms the dossier is complete.</summary>
    internal sealed class EnPlatformConfirmStep : EnStep
    {
        public override string Code => "ENG.PLATFORM.CONFIRM";
        public override string Title => "تایید کامل بودن مدارک پلتفرم";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            var rows = await uow.Set<EnPlatformDossier>().AsNoTracking().Where(d => !d.Deleted && d.Status == EnDossierStatus.Completing)
                .OrderBy(d => d.Id).Take(ProcStepCommon.Cap).Select(d => new { d.Id, Code = uow.Set<PbProduct>().Where(p => p.Id == d.ProductId).Select(p => p.Code).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnPlatformDossier", r.Id, r.Code, null, null, $"مدارک پلتفرم {r.Code} کامل اعلام شده و منتظر تایید مدیر مهندسی است.", EnRoles.RefApprovers)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.RefApprovers, Link = Root + "/platforms", Title = "تایید مدارک پلتفرم " + item.TargetNumber };
    }

    /// <summary>The small text helper of the steps (the service-side helper is internal to the same assembly).</summary>
    internal static class EnText2
    {
        public static string Cut(string s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max);
        public static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
