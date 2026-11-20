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
            services.AddScoped<IAgentStep, EnPlatformStandardStep>();
            services.AddScoped<IAgentStep, EnPlatformCompleteStep>();
            services.AddScoped<IAgentStep, EnPlatformConfirmStep>();
            services.AddScoped<IAgentStep, EnCaseOpenStep>();
            services.AddScoped<IAgentStep, EnCaseStandardStep>();
            services.AddScoped<IAgentStep, EnCaseProposeStep>();
            services.AddScoped<IAgentStep, EnCaseClassifyInputStep>();
            services.AddScoped<IAgentStep, EnCaseConfirmStep>();
            services.AddScoped<IAgentStep, EnOpinionInputStep>();
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
            services.AddScoped<IAgentStep, EnDistDraftStep>();
            services.AddScoped<IAgentStep, EnDistNotifyStep>();
            services.AddScoped<IAgentStep, EnDistAckStep>();
            services.AddScoped<IAgentStep, EnInfoAnswerStep>();
            services.AddScoped<IAgentStep, EnPlatformRegisterStep>();
            services.AddScoped<IAgentStep, EnDevManagerStep>();
            services.AddScoped<IAgentStep, EnDevCeoStep>();
            services.AddScoped<IAgentStep, EnRiskWatchStep>();
            services.AddScoped<IAgentStep, EnRiskEscalateStep>();
            services.AddScoped<IAgentStep, EnStandardGapStep>();
            services.AddScoped<IAgentStep, EnStandardGapConfirmStep>();
            return services;
        }
    }

    internal abstract class EnStep : ProcStep, IOfficeDeadlineStep
    {
        /// <summary>Employer rule 14: a human gate of engineering that waits longer than this many OFFICE hours is a permanent alarm of the CEO (ENG.RISK.ESCALATE). 0 = the step has its own deadline.</summary>
        public virtual int OfficeDeadlineHours => global::Services.NasbNiro.Office.OfficeClock.StopLimitHours;

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
        public override string Title => "ایجنت مهندسی: پیش‌نویس فهرست مدارک مرجع (MDL) از الگوی MDR";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<AgentWorkItem> items = new List<AgentWorkItem>();
            if (!await EnControl.ActiveAsync(uow, ct)) return items;   // the engineering control has not started (or starts later): nothing to do yet
            bool any = await uow.Set<EnReferenceList>().AsNoTracking().AnyAsync(l => !l.Deleted && (l.Status == EnRefStatus.Draft || l.Status == EnRefStatus.UnderReview || l.Status == EnRefStatus.Approved), ct);
            if (!any)
            {
                var t = await uow.Set<PcMdrTemplate>().AsNoTracking().Where(x => !x.Deleted && x.IsActive && x.Lines.Any()).OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Title }).FirstOrDefaultAsync(ct);
                if (t != null) items.Add(Item("PcMdrTemplate", t.Id, t.Code, null, null, "فهرست MDL وجود ندارد؛ الگوی MDR «" + t.Title + "» آمادهٔ تبدیل به پیش‌نویس است."));
            }
            // a case under a standard that has no list in any status: the standard-gap report cannot compare; the agent drafts that list as a copy of the platform's base list for the engineers to adapt
            foreach (var (c, p) in await EnGaps.TargetListMissingAsync(uow, ProcStepCommon.Cap, ct))
                items.Add(Item("EnCase", c.Id, c.Number, null, null, "فهرست MDL استاندارد " + c.ReferenceCode + " در هیچ وضعیتی وجود ندارد؛ پیش‌نویس آن از فهرست استاندارد " + p.BaseCode + " ساخته می‌شود."));
            return items.Take(ProcStepCommon.Cap).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            if (item.TargetType == "EnCase") return await DraftForGapAsync(item, uow, services, ct);
            PcMdrTemplate t = await uow.Set<PcMdrTemplate>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == item.TargetId && !x.Deleted, ct);
            if (t == null || t.Lines.Count == 0) return AgentStepResult.Waiting("الگوی MDR پیدا نشد یا ردیفی ندارد.");
            EnSaveReferenceDto dto = new EnSaveReferenceDto
            {
                Code = EnText2.Cut("VDDR-" + t.Code, 60), Title = EnText2.Cut("فهرست مدارک مرجع (MDL) — " + t.Title, 300), StandardSource = EnText2.Cut(t.StandardSource, 200),
                Note = "پیش‌نویس خودکار از الگوی MDR؛ مدارک لازم در مرحلهٔ مناقصه را مهندسی علامت بزند.",
                Lines = t.Lines.OrderBy(l => l.Seq).Select(l => new EnReferenceLineDto { DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, Required = l.Required, ClientSubmittal = l.ClientSubmittal, StandardClause = EnText2.Cut(l.StandardSource, 300) }).ToList(),
            };
            ResponseDto<EnReferenceListDto> response = await services.GetRequiredService<IEnReferenceService>().SaveListAsync(dto, ct);
            if (!response.Success) return ProcStepCommon.Refusal(response);
            return AgentStepResult.Done("EnReferenceList", response.Data.Id, response.Data.Code, $"ایجنت مهندسی پیش‌نویس فهرست MDL ({response.Data.LineCount} ردیف) را از الگوی MDR ساخت؛ مهندسی آن را بررسی و ارسال می‌کند.");
        }

        private static async Task<AgentStepResult> DraftForGapAsync(AgentWorkItem item, IUnitOfWork uow, IServiceProvider services, CancellationToken ct)
        {
            EnCase c = await uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.TargetId && !x.Deleted, ct);
            if (c == null) return AgentStepResult.Waiting("پروندهٔ مهندسی پیدا نشد.");
            EnGaps.Plan p = await EnGaps.PlanOfAsync(uow, c, ct);
            if (!p.Needed || p.TargetAnyList || p.BaseCode == null) return AgentStepResult.Waiting("فهرست این استاندارد دیگر لازم نیست یا از پیش وجود دارد.");
            EnReferenceList baseList = await uow.Set<EnReferenceList>().AsNoTracking().Include(l => l.Lines).Where(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == p.BaseCode).FirstOrDefaultAsync(ct);
            if (baseList == null) return AgentStepResult.Waiting("فهرست تاییدشدهٔ استاندارد پایه پیدا نشد.");
            EnSaveReferenceDto dto = new EnSaveReferenceDto
            {
                Code = EnText2.Cut(c.ReferenceCode, 60), Title = EnText2.Cut("فهرست مدارک مرجع (MDL) — " + c.ReferenceCode, 300), StandardSource = EnText2.Cut(baseList.StandardSource, 200),
                Note = EnText2.Cut("نسخهٔ بررسی‌نشده از " + p.BaseCode + " که باید با متن استاندارد " + c.ReferenceCode + " تطبیق داده شود.", 1000),
                Lines = baseList.Lines.OrderBy(l => l.Seq).Select(l => new EnReferenceLineDto { DocumentType = l.DocumentType, Discipline = l.Discipline, Title = l.Title, Required = l.Required, TenderStage = l.TenderStage, ClientSubmittal = l.ClientSubmittal, StandardClause = l.StandardClause, Note = l.Note, Units = l.Units }).ToList(),
            };
            ResponseDto<EnReferenceListDto> response = await services.GetRequiredService<IEnReferenceService>().SaveListAsync(dto, ct);
            if (!response.Success) return ProcStepCommon.Refusal(response);
            return AgentStepResult.Done("EnReferenceList", response.Data.Id, response.Data.Code, $"ایجنت مهندسی پیش‌نویس فهرست استاندارد {response.Data.Code} را (کپی بررسی‌نشدهٔ {p.BaseCode}) ساخت تا مهندسی آن را با متن استاندارد تطبیق دهد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/reference", Title = "تهیهٔ فهرست مدارک مرجع (MDL)" };
    }

    /// <summary>ENG.REF.INPUT (Input): a draft list that nobody has sent for approval (the engineers correct it, mark the tender stage documents and send it).</summary>
    internal sealed class EnRefInputStep : EnStep
    {
        public override string Code => "ENG.REF.INPUT";
        public override string Title => "بازبینی و ارسال فهرست مدارک مرجع (MDL) (محتوای مهندسی)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            var rows = await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Draft).OrderBy(l => l.Id).Take(ProcStepCommon.Cap).Select(l => new { l.Id, l.Code, l.Revision, Lines = l.Lines.Count }).ToListAsync(ct);
            return rows.Select(r => Item("EnReferenceList", r.Id, r.Code, null, null, $"فهرست MDL {r.Code} (بازنگری {r.Revision}، {r.Lines} ردیف) پیش‌نویس است و برای تایید ارسال نشده است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/reference/" + Id(item.TargetId), Title = "بازبینی و ارسال فهرست مدارک مرجع (MDL) " + item.TargetNumber };
    }

    /// <summary>ENG.REF.APPROVE (HumanGate): the engineering manager or the CEO, who did not touch the list, approves it.</summary>
    internal sealed class EnRefApproveStep : EnStep
    {
        public override string Code => "ENG.REF.APPROVE";
        public override string Title => "تایید فهرست مدارک مرجع (MDL)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            var rows = await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.UnderReview).OrderBy(l => l.Id).Take(ProcStepCommon.Cap).Select(l => new { l.Id, l.Code, l.Revision }).ToListAsync(ct);
            return rows.Select(r => Item("EnReferenceList", r.Id, r.Code, null, null, $"فهرست MDL {r.Code} (بازنگری {r.Revision}) منتظر تایید است.", EnRoles.RefApprovers)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.RefApprovers, Link = Root + "/reference/" + Id(item.TargetId), Title = "تایید فهرست مدارک مرجع (MDL) " + item.TargetNumber };
    }

    // ------------------------------------------------------------------------------------------ the platform dossiers

    internal static class EnCurrentList
    {
        /// <summary>The current approved list of each Code: (Code, Id) pairs.</summary>
        public static async Task<List<KeyValuePair<string, int>>> CurrentAsync(IUnitOfWork uow, CancellationToken ct)
        {
            var rows = await uow.Set<EnReferenceList>().AsNoTracking().Where(l => !l.Deleted && l.Status == EnRefStatus.Approved).Select(l => new { l.Code, l.Id, l.ApprovedAt }).ToListAsync(ct);
            return rows.GroupBy(r => r.Code).Select(g => new KeyValuePair<string, int>(g.Key, g.OrderByDescending(r => r.ApprovedAt).ThenByDescending(r => r.Id).First().Id)).OrderBy(p => p.Key).ToList();
        }
    }

    /// <summary>ENG.PLATFORM.ASSESS (Agent): a dossier (product, standard) whose standard has an Approved list and that is not yet built against its current revision. The agent builds the lines and proves what the system knows (a released BOM, an approved routing).</summary>
    internal sealed class EnPlatformAssessStep : EnStep
    {
        public override string Code => "ENG.PLATFORM.ASSESS";
        public override string Title => "ایجنت مهندسی: ارزیابی مدارک پلتفرم نسبت به فهرست MDL";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();   // the engineering control has not started (or starts later): nothing to do yet
            var current = await EnCurrentList.CurrentAsync(uow, ct);
            if (current.Count == 0 && !EnControl.Probe) return new List<AgentWorkItem>();
            var rows = await uow.Set<EnPlatformDossier>().AsNoTracking().Where(d => !d.Deleted && uow.Set<PbProduct>().Any(p => p.Id == d.ProductId && p.IsActive && !p.Deleted)
                    && uow.Set<EnReferenceList>().Any(l => !l.Deleted && l.Status == EnRefStatus.Approved && l.Code == d.ReferenceCode && l.Id != d.ReferenceListId))
                .OrderBy(d => d.Id).Take(ProcStepCommon.Cap).Select(d => new { d.Id, d.ProductId, d.ReferenceCode, Code = uow.Set<PbProduct>().Where(p => p.Id == d.ProductId).Select(p => p.Code).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnPlatformDossier", r.Id, r.Code + "/" + r.ReferenceCode, null, null, $"پرونده مدارک پلتفرم {r.Code} برای استاندارد {r.ReferenceCode} نسبت به فهرست MDL جاری ارزیابی نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            var d = await uow.Set<EnPlatformDossier>().AsNoTracking().Where(x => x.Id == item.TargetId).Select(x => new { x.ProductId, x.ReferenceCode }).FirstOrDefaultAsync(ct);
            if (d == null) return AgentStepResult.Waiting("پرونده پیدا نشد.");
            ResponseDto<EnDossierDto> response = await services.GetRequiredService<IEnPlatformService>().DossierActionAsync(new EnDossierActionDto { ProductId = d.ProductId, ReferenceCode = d.ReferenceCode, Action = "assess" }, ct);
            if (!response.Success) return ProcStepCommon.Refusal(response);
            return AgentStepResult.Done("EnPlatformDossier", response.Data.Id, item.TargetNumber, $"ایجنت مهندسی پرونده {item.TargetNumber} را ارزیابی کرد: {response.Data.MissingCount} مدرک الزامی کم است از {response.Data.RequiredCount}.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/platforms", Title = "ارزیابی مدارک پلتفرم " + item.TargetNumber };
    }

    /// <summary>ENG.PLATFORM.STANDARD (Input): an active product that has no dossier at all. Only the people know the standard of the data sheet the platform was built for: they record it (with the evidence); until then the platform is NotAssessed.</summary>
    internal sealed class EnPlatformStandardStep : EnStep
    {
        public override string Code => "ENG.PLATFORM.STANDARD";
        public override string Title => "ثبت استاندارد دیتاشیت اصلی پلتفرم (مبنای ساخت)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            if (!await EnControl.ActiveAsync(uow, ct)) return new List<AgentWorkItem>();
            var rows = await uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && !uow.Set<EnPlatformDossier>().Any(d => d.ProductId == p.Id && !d.Deleted) && !uow.Set<EnPlatformRegistration>().Any(g => g.ProductId == p.Id && !g.Deleted && g.Status == EnRegStatus.Proposed)
                    && !EnOrigin.UnregisteredNewProducts(uow).Any(u => u.Id == p.Id))   // a New design becomes a platform only through the CEO-approved registration
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Code }).ToListAsync(ct);
            return rows.Select(r => Item("PbProduct", r.Id, r.Code, null, null, $"استاندارد دیتاشیت پروژه‌ای که پلتفرم {r.Code} برای آن ساخته شد را ثبت کنید.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/platforms/" + Id(item.TargetId), Title = "ثبت استاندارد دیتاشیت پلتفرم " + item.TargetNumber };
    }

    /// <summary>ENG.PLATFORM.REGISTER (HumanGate, CEO): the head of engineering proposed that the product of a New design becomes a platform; only the CEO approves (never an AI seat). The request waits without expiring.</summary>
    internal sealed class EnPlatformRegisterStep : EnStep
    {
        public override string Code => "ENG.PLATFORM.REGISTER";
        public override string Title => "تصمیم مدیرعامل دربارهٔ پلتفرم‌شدن طرح جدید";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnPlatformRegistration>().AsNoTracking().Where(x => !x.Deleted && x.Status == EnRegStatus.Proposed).OrderBy(x => x.Id).Take(ProcStepCommon.Cap).Select(x => new { x.Id, x.Number, x.ReferenceCode }).ToListAsync(ct);
            return rows.Select(r => Item("EnPlatformRegistration", r.Id, r.Number, null, null, $"پیشنهاد ثبت طرح جدید به عنوان پلتفرم ({r.Number}، استاندارد {r.ReferenceCode}) منتظر تصمیم مدیرعامل است.", EnRoles.Ceo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Ceo, Link = Root + "/platform-registrations/" + Id(item.TargetId), Title = "تصمیم دربارهٔ پلتفرم‌شدن طرح جدید " + item.TargetNumber };
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
            var current = await EnCurrentList.CurrentAsync(uow, ct);
            if (current.Count == 0 && !EnControl.Probe) return new List<AgentWorkItem>();
            List<int> ids = current.Select(c => c.Value).ToList();
            var rows = await uow.Set<EnPlatformDossier>().AsNoTracking().Where(d => !d.Deleted && d.Status == EnDossierStatus.Incomplete && d.ReferenceListId != null && ids.Contains(d.ReferenceListId.Value) && uow.Set<PbProduct>().Any(p => p.Id == d.ProductId && p.IsActive && !p.Deleted))
                .OrderBy(d => d.Id).Take(ProcStepCommon.Cap).Select(d => new { d.Id, d.ProductId, Code = uow.Set<PbProduct>().Where(p => p.Id == d.ProductId).Select(p => p.Code).FirstOrDefault() + "/" + d.ReferenceCode, Missing = d.Documents.Count(x => x.Required && x.State == EnLineState.Missing) }).ToListAsync(ct);
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
                .OrderBy(d => d.Id).Take(ProcStepCommon.Cap).Select(d => new { d.Id, Code = uow.Set<PbProduct>().Where(p => p.Id == d.ProductId).Select(p => p.Code).FirstOrDefault() + "/" + d.ReferenceCode }).ToListAsync(ct);
            return rows.Select(r => Item("EnPlatformDossier", r.Id, r.Code, null, null, $"مدارک پلتفرم {r.Code} کامل اعلام شده و منتظر تایید مدیر مهندسی است.", EnRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = Root + "/platforms", Title = "تایید مدارک پلتفرم " + item.TargetNumber };
    }

    // ------------------------------------------------------------------------------------------ the early warning (an emergency should never happen)

    /// <summary>ENG.RISK.WATCH (Input, owned by the engineering agent): one open task per controlled, not-closed case with a document risk. It closes only when the risk is gone, never by an acknowledgement.</summary>
    internal sealed class EnRiskWatchStep : EnStep
    {
        public override string Code => "ENG.RISK.WATCH";
        public override string Title => "هشدار زودهنگام: خطر مدارک پرونده مهندسی (باید دیده و پیگیری شود)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Manager;
        public override int OfficeDeadlineHours => 0;   // the early warning has its own escalation (ENG.RISK.ESCALATE)

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            List<EnRiskRow> rows = await EnRisk.ComputeAsync(uow, ct);
            return rows.Take(ProcStepCommon.Cap).Select(r => Item("EnCase", r.CaseId, r.Number, r.ContractId, null, r.Summary(), EnRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = Root + "/cases/" + Id(item.TargetId), Title = "خطر مدارک پروندهٔ " + item.TargetNumber };
    }

    /// <summary>ENG.RISK.ESCALATE (Agent): keeps the inbox of the CEO equal to the truth: an open item for every case at document risk from Awarded on and for every overdue document debt, until the risk is gone or the debt is settled. A person cannot silence it by reading it.</summary>
    internal sealed class EnRiskEscalateStep : EnStep
    {
        public override string Code => "ENG.RISK.ESCALATE";
        public override string Title => "ایجنت مهندسی: اعلام دائمی خطر مدارک و بدهی سررسیدشده به مدیرعامل";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Manager;

        private static AgentWorkItem ItemOf(string key, bool open, Func<string, string, int, string, AgentWorkItem> make)
        {
            // three key kinds: the risk of a case, an overdue document debt, an engineering task that waited more than 72 office hours
            bool risk = key.StartsWith(EnRisk.RiskKeyPrefix, StringComparison.Ordinal);
            bool stall = key.StartsWith(EnRisk.StallKeyPrefix, StringComparison.Ordinal);
            string prefix = risk ? EnRisk.RiskKeyPrefix : stall ? EnRisk.StallKeyPrefix : EnRisk.DebtKeyPrefix;
            int id = int.Parse(key.Substring(prefix.Length), CultureInfo.InvariantCulture);
            return make(risk ? "EnCase" : stall ? "AgentTask" : "EnDocumentDebt", key, id, open
                ? (stall ? "کار منتظرمانده بیش از ۷۲ ساعت اداری برای مدیرعامل در کارتابل اعلام می‌شود." : "هشدار مدارک برای مدیرعامل در کارتابل باز می‌شود.")
                : (stall ? "کار دیگر منتظر یا ناموفق نیست؛ هشدار کارتابل مدیرعامل بسته می‌شود." : "خطر یا بدهی برطرف شده؛ هشدار کارتابل مدیرعامل بسته می‌شود."));
        }

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            HashSet<string> desired = await EnRisk.DesiredKeysAsync(uow, ct);
            List<string> open = await EnRisk.OpenKeysAsync(uow, ct);
            List<AgentWorkItem> items = new List<AgentWorkItem>();
            foreach (string k in desired.Except(open).OrderBy(x => x, StringComparer.Ordinal)) items.Add(ItemOf(k, true, (t, n, id, s) => Item(t, id, n, null, null, s)));
            foreach (string k in open.Except(desired).OrderBy(x => x, StringComparer.Ordinal)) items.Add(ItemOf(k, false, (t, n, id, s) => Item(t, id, n, null, null, s)));
            return items.Take(ProcStepCommon.Cap).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            string key = item.TargetNumber;
            bool risk = key.StartsWith(EnRisk.RiskKeyPrefix, StringComparison.Ordinal);
            bool stall = key.StartsWith(EnRisk.StallKeyPrefix, StringComparison.Ordinal);
            string targetType = risk ? nameof(EnCase) : stall ? nameof(AgentTask) : nameof(EnDocumentDebt);
            // the risk of this one case only (no run over all cases per task): the same rule as the full computation
            EnRiskRow row = risk ? (await EnRisk.ComputeAsync(uow, ct, new[] { item.TargetId })).FirstOrDefault() : null;
            EnStalledTask stalled = stall ? (await EnRisk.StalledTasksAsync(uow, ct)).FirstOrDefault(x => x.TaskId == item.TargetId) : null;
            bool waiting = risk ? row != null && row.Awarded : stall ? stalled != null : (await EnRisk.OverdueDebtsAsync(uow, ct)).Any(d => d.Id == item.TargetId);
            string title; string link;
            if (risk)
            {
                title = row != null ? "خطر مدارک: " + row.Summary() : "خطر مدارک پرونده مهندسی " + Id(item.TargetId);
                link = Root + "/cases/" + Id(item.TargetId);
            }
            else if (stall)
            {
                // the title and link come from the task itself (also when it just left the state: the closing needs no text)
                AgentTask task = stalled == null ? await uow.Set<AgentTask>().AsNoTracking().Where(t => t.Id == item.TargetId).FirstOrDefaultAsync(ct) : null;
                string taskTitle = stalled?.Title ?? task?.Title ?? "کار " + Id(item.TargetId);
                title = stalled != null && stalled.Failed
                    ? "کار ایجنت «" + taskTitle + "» بیش از ۷۲ ساعت اداری ناموفق مانده است"
                    : "کار «" + taskTitle + "» بیش از ۷۲ ساعت اداری منتظر " + (stalled?.RolesText ?? "مسئول کار") + " مانده است؛ تصمیم یا پیگیری مدیرعامل لازم است";
                link = string.IsNullOrWhiteSpace(stalled?.Link ?? task?.Link) ? Root : (stalled?.Link ?? task?.Link);
            }
            else
            {
                string debt = await uow.Set<EnDocumentDebt>().AsNoTracking().Where(d => d.Id == item.TargetId).Select(d => d.Title).FirstOrDefaultAsync(ct);
                title = "بدهی مدرک «" + debt + "» از سررسید گذشته و تسویه نشده است";
                link = Root + "/debts";
            }
            await global::Services.NasbNiro.Office.OfficeGates.SyncAsync(services.GetService<global::Services.NasbNiro.Office.INotificationService>(), key, waiting, "Engineering", targetType, item.TargetId, title, link, EnRoles.Ceo, ct, null, global::Entities.Model.NasbNiro.Office.OfInboxKind.Task, global::Entities.Model.NasbNiro.Office.OfUrgency.Urgent);
            return AgentStepResult.Done(targetType, item.TargetId, key, waiting ? "ایجنت مهندسی هشدار دائمی را در کارتابل مدیرعامل باز کرد." : "ایجنت مهندسی هشدار را بست؛ خطر برطرف شده است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = Root + "/cases", Title = "هشدار مدارک " + item.TargetNumber };
    }

    // ------------------------------------------------------------------------------------------ standards deviations

    internal abstract class EnDeviationStep : EnStep
    {
        protected abstract EnDeviationStatus Waiting { get; }
        protected abstract string Roles { get; }
        public override string FallbackRoles => null;
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            EnDeviationStatus status = Waiting;
            var rows = await uow.Set<EnDeviation>().AsNoTracking().Where(d => !d.Deleted && d.Status == status).OrderBy(d => d.Id).Take(ProcStepCommon.Cap).Select(d => new { d.Id, d.Number, d.StandardCode, d.Requirement }).ToListAsync(ct);
            string roles = Roles;
            return rows.Select(r => Item("EnDeviation", r.Id, r.Number, null, null, $"انحراف از استاندارد {r.StandardCode} ({r.Number}): «{EnText2.Cut(r.Requirement, 120)}» منتظر تصمیم است.", roles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = Roles, Link = Root + "/deviations/" + Id(item.TargetId), Title = "تصمیم دربارهٔ انحراف از استاندارد " + item.TargetNumber };
    }

    /// <summary>ENG.DEV.MANAGER (HumanGate): the head of engineering decides first on an instruction that contradicts a requirement of a standard.</summary>
    internal sealed class EnDevManagerStep : EnDeviationStep
    {
        public override string Code => "ENG.DEV.MANAGER";
        public override string Title => "تصمیم مدیر مهندسی دربارهٔ انحراف از استاندارد";
        protected override EnDeviationStatus Waiting => EnDeviationStatus.Requested;
        protected override string Roles => EnRoles.Manager;
    }

    /// <summary>ENG.DEV.CEO (HumanGate): the CEO decides after the head of engineering (a different person); only then is the instruction executed.</summary>
    internal sealed class EnDevCeoStep : EnDeviationStep
    {
        public override string Code => "ENG.DEV.CEO";
        public override string Title => "تصمیم مدیرعامل دربارهٔ انحراف از استاندارد (پس از مدیر مهندسی)";
        protected override EnDeviationStatus Waiting => EnDeviationStatus.ManagerApproved;
        protected override string Roles => EnRoles.Ceo;
    }

    // ------------------------------------------------------------------------------------------ the standard-gap report

    /// <summary>ENG.STANDARD.GAP (Agent): a platform ordered under another standard gets its gap report at once (idempotent: one live report per case and revision pair; a new Approved revision voids and rebuilds it).</summary>
    internal sealed class EnStandardGapStep : EnStep
    {
        public override string Code => "ENG.STANDARD.GAP";
        public override string Title => "ایجنت اصلی: گزارش تفاوت استاندارد سفارش با استاندارد پلتفرم";

        /// <summary>The employer's rule: the MAIN agent builds the standard-gap report. The master seat writes only EnStandardGap, EnStandardGapLine and EnCaseEvent here (SeatApprovalGuard); it never writes a list, a decision or a confirmation.</summary>
        public override string AgentCode => AgentCodes.Master;
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Manager;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            EnCaseStatus a = EnCaseStatus.AwaitingEngConfirm, b = EnCaseStatus.AwaitingCeoApproval;
            List<EnCase> cases = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && (c.Status == a || c.Status == b) && (c.ProposedClassification == EnClassification.Platform || uow.Set<EnStandardGap>().Any(g => g.CaseId == c.Id && !g.Deleted && g.Status != EnGapStatus.Void))).OrderBy(c => c.Id).Take(200).ToListAsync(ct);
            List<AgentWorkItem> items = new List<AgentWorkItem>();
            foreach (EnCase c in cases)
            {
                EnGaps.Plan p = await EnGaps.PlanOfAsync(uow, c, ct);
                EnStandardGap live = await EnGaps.LiveAsync(uow, c.Id, ct);
                bool work = p.Needed ? live == null || !EnGaps.Same(live, p, c.ReferenceCode) : live != null;
                if (work) items.Add(Item("EnCase", c.Id, c.Number, null, null, "گزارش تفاوت استاندارد " + c.ReferenceCode + " برای پروندهٔ " + c.Number + " باید ساخته یا به‌روز شود."));
                if (items.Count >= ProcStepCommon.Cap) break;
            }
            return items;
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<string> built = await services.GetRequiredService<IEnStandardGapService>().BuildAsync(item.TargetId, ct);
            if (!built.Success) return ProcStepCommon.Refusal(built);
            return AgentStepResult.Done("EnCase", item.TargetId, item.TargetNumber, built.Data);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = Root + "/cases/" + Id(item.TargetId), Title = "گزارش تفاوت استاندارد " + item.TargetNumber };
    }

    /// <summary>ENG.STANDARD.GAP.CONFIRM (HumanGate): the head of engineering states the effect on materials and design; the CEO cannot approve the case before.</summary>
    internal sealed class EnStandardGapConfirmStep : EnStep
    {
        public override string Code => "ENG.STANDARD.GAP.CONFIRM";
        public override string Title => "تایید گزارش تفاوت استاندارد توسط مدیر مهندسی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnStandardGap>().AsNoTracking().Where(g => !g.Deleted && g.Status == EnGapStatus.Draft).OrderBy(g => g.Id).Take(ProcStepCommon.Cap).Select(g => new { g.Id, g.Number, g.Summary }).ToListAsync(ct);
            return rows.Select(r => Item("EnStandardGap", r.Id, r.Number, null, null, EnText2.Cut(r.Summary, 200), EnRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = Root + "/standard-gaps/" + Id(item.TargetId), Title = "تایید گزارش تفاوت استاندارد " + item.TargetNumber };
    }

    /// <summary>The small text helper of the steps (the service-side helper is internal to the same assembly).</summary>
    internal static class EnText2
    {
        public static string Cut(string s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max);
        public static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
