using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Engineering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // ENG-E1g: the MDL of a PROJECT in the tender (employer rule 20), station 1 (sales and tender). The engineering agent drafts it from the approved standard list; an engineer checks, the engineering manager approves,
    // commercial records the client's approval after the release; a special project goes without it only by the CEO's permit. Every step is under the 72 office hour rule of EnStep.

    public static class EngineeringProjectMdlSteps
    {
        public static IServiceCollection AddEngineeringProjectMdlSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, EnPmdlDraftStep>();
            services.AddScoped<IAgentStep, EnPmdlCheckStep>();
            services.AddScoped<IAgentStep, EnPmdlApproveStep>();
            services.AddScoped<IAgentStep, EnPmdlPermitDecideStep>();
            services.AddScoped<IAgentStep, EnPmdlClientStep>();
            return services;
        }
    }

    internal abstract class EnPmdlStep : EnStep
    {
        public override int Station => 1;
        protected static string CaseLink(int id) => Root + "/cases/" + Id(id);
    }

    /// <summary>ENG.PMDL.DRAFT (Agent): a turbine case in the tender preparation whose standard has an approved standard list and that has no live project MDL and no approved "without MDL" permit: the agent drafts the project list (the same rule as the service's draft).</summary>
    internal sealed class EnPmdlDraftStep : EnPmdlStep
    {
        public override string Code => "ENG.PMDL.DRAFT";
        public override string Title => "ایجنت مهندسی: پیش‌نویس فهرست مدارک پروژه (MDL) مناقصه از فهرست استاندارد";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            HashSet<string> approved = await EnReferenceRules.ApprovedSetAsync(uow, ct);
            if (approved.Count == 0) return new List<AgentWorkItem>();
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Kind == EnCaseKind.Turbine && c.Status == EnCaseStatus.TenderPreparation && c.ReferenceCode != null && c.ReferenceCode != ""
                    && !uow.Set<EnProjectMdl>().Any(m => m.CaseId == c.Id && !m.Deleted && m.Status != EnProjectMdlStatus.Void)
                    && !uow.Set<EnMdlPermit>().Any(p => p.CaseId == c.Id && !p.Deleted && p.Status == EnMdlPermitStatus.Approved))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId, c.ReferenceCode }).ToListAsync(ct);
            return rows.Where(r => EnReferenceRules.IsApproved(approved, r.ReferenceCode))
                .Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"برای پروندهٔ {r.Number} فهرست مدارک پروژه (MDL) از فهرست استاندارد «{r.ReferenceCode}» هنوز پیش‌نویس نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<EnProjectMdlCaseDto> r = await services.GetRequiredService<IEnProjectMdlService>().ActionAsync(new EnProjectMdlActionDto { Action = "draft", CaseId = item.TargetId }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("EnCase", item.TargetId, item.TargetNumber, $"ایجنت مهندسی فهرست مدارک پروژه {r.Data.Mdl?.Number} ({EnText.Num(r.Data.Mdl?.LineCount ?? 0)} ردیف) را برای پروندهٔ {item.TargetNumber} از فهرست استاندارد ساخت.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "پیش‌نویس فهرست مدارک پروژه " + item.TargetNumber };
    }

    /// <summary>ENG.PMDL.CHECK (HumanGate): an engineering expert checks a Draft list (he may first replace its lines from a file); the agent never checks.</summary>
    internal sealed class EnPmdlCheckStep : EnPmdlStep
    {
        public override string Code => "ENG.PMDL.CHECK";
        public override string Title => "بررسی فهرست مدارک پروژه (MDL) توسط کارشناس مهندسی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnProjectMdl>().AsNoTracking().Where(m => !m.Deleted && m.Status == EnProjectMdlStatus.Draft && m.Lines.Any(l => !l.Deleted && !l.Removed)
                    && uow.Set<EnCase>().Any(c => c.Id == m.CaseId && !c.Deleted && c.Status == EnCaseStatus.TenderPreparation))
                .OrderBy(m => m.Id).Take(ProcStepCommon.Cap).Select(m => new { m.Id, m.Number, m.CaseId, m.Revision, CaseNumber = uow.Set<EnCase>().Where(c => c.Id == m.CaseId).Select(c => c.Number).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnProjectMdl", r.Id, r.Number, null, null, $"فهرست مدارک پروژه {r.Number} (پروندهٔ {r.CaseNumber}، ویرایش {EnText.Num(r.Revision)}) منتظر بررسی کارشناس مهندسی است؛ ردیف‌ها فقط با فایل تغییر می‌کند.", EnRoles.Writers)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/cases", Title = "بررسی فهرست مدارک پروژه " + item.TargetNumber };
    }

    /// <summary>ENG.PMDL.APPROVE (HumanGate): the engineering manager (not the checker) approves a Checked list or returns it.</summary>
    internal sealed class EnPmdlApproveStep : EnPmdlStep
    {
        public override string Code => "ENG.PMDL.APPROVE";
        public override string Title => "تایید فهرست مدارک پروژه (MDL) توسط مدیر مهندسی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnProjectMdl>().AsNoTracking().Where(m => !m.Deleted && m.Status == EnProjectMdlStatus.Checked
                    && uow.Set<EnCase>().Any(c => c.Id == m.CaseId && !c.Deleted && c.Status == EnCaseStatus.TenderPreparation))
                .OrderBy(m => m.Id).Take(ProcStepCommon.Cap).Select(m => new { m.Id, m.Number, m.CaseId, m.Revision, CaseNumber = uow.Set<EnCase>().Where(c => c.Id == m.CaseId).Select(c => c.Number).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnProjectMdl", r.Id, r.Number, null, null, $"فهرست مدارک پروژه {r.Number} (پروندهٔ {r.CaseNumber}، ویرایش {EnText.Num(r.Revision)}) بررسی شده و منتظر تایید مدیر مهندسی است؛ بستهٔ فنی مناقصه تا تایید آن منتشر نمی‌شود.", EnRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = Root + "/cases", Title = "تایید فهرست مدارک پروژه " + item.TargetNumber };
    }

    /// <summary>ENG.PMDL.PERMIT.DECIDE (HumanGate): the CEO alone decides a requested "tender without MDL" permit (not its requester).</summary>
    internal sealed class EnPmdlPermitDecideStep : EnPmdlStep
    {
        public override string Code => "ENG.PMDL.PERMIT.DECIDE";
        public override string Title => "تصمیم مدیرعامل دربارهٔ مجوز مناقصه بدون فهرست مدارک پروژه (MDL)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnMdlPermit>().AsNoTracking().Where(p => !p.Deleted && p.Status == EnMdlPermitStatus.Requested
                    && uow.Set<EnCase>().Any(c => c.Id == p.CaseId && !c.Deleted && c.Status == EnCaseStatus.TenderPreparation))
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Number, p.CaseId, CaseNumber = uow.Set<EnCase>().Where(c => c.Id == p.CaseId).Select(c => c.Number).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnMdlPermit", r.Id, r.Number, null, null, $"مدیر مهندسی برای پروندهٔ {r.CaseNumber} مجوز «مناقصه بدون MDL» ({r.Number}) خواسته و فقط مدیرعامل تصمیم می‌گیرد.", EnRoles.Ceo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Ceo, Link = Root + "/cases", Title = "تصمیم دربارهٔ مجوز مناقصه بدون MDL " + item.TargetNumber };
    }

    /// <summary>ENG.PMDL.CLIENT (Input): the approved list went to the client with the released tender package; commercial records the client's approval (letter reference and the letter as a file). Open while it is not recorded.</summary>
    internal sealed class EnPmdlClientStep : EnPmdlStep
    {
        public override string Code => "ENG.PMDL.CLIENT";
        public override string Title => "ثبت تایید کارفرما بر فهرست مدارک پروژه (MDL) توسط بازرگانی";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Commercial;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnProjectMdl>().AsNoTracking().Where(m => !m.Deleted && m.Status == EnProjectMdlStatus.Approved && m.ClientApprovedAt == null
                    && uow.Set<EnCase>().Any(c => c.Id == m.CaseId && !c.Deleted && c.TenderPackageId != null
                        && (c.Status == EnCaseStatus.TenderIssued || c.Status == EnCaseStatus.Awarded || c.Status == EnCaseStatus.InEngineering || c.Status == EnCaseStatus.HandedOver || c.Status == EnCaseStatus.Accepted || c.Status == EnCaseStatus.Released)))
                .OrderBy(m => m.Id).Take(ProcStepCommon.Cap).Select(m => new { m.Id, m.Number, m.CaseId, m.Revision, CaseNumber = uow.Set<EnCase>().Where(c => c.Id == m.CaseId).Select(c => c.Number).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnProjectMdl", r.Id, r.Number, null, null, $"فهرست مدارک پروژه {r.Number} (پروندهٔ {r.CaseNumber}، ویرایش {EnText.Num(r.Revision)}) با بستهٔ فنی به کارفرما رسیده؛ تایید کارفرما (شمارهٔ نامه و فایل نامه) را ثبت کنید.", EnRoles.Commercial)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Commercial, Link = Root + "/cases", Title = "ثبت تایید کارفرما بر فهرست مدارک پروژه " + item.TargetNumber };
    }
}
