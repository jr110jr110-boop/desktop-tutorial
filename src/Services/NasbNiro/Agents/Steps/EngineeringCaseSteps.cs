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
using Entities.Model.NasbNiro.Engineering;
using Entities.Model.NasbNiro.ProductBank;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Engineering;
using ViewModels.NasbNiro.EngineeringDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // ENG-E1: the case (station 1: sales and tender, owned by the engineering agent) and the charter review (station 3).

    internal abstract class EnCaseStep : EnStep
    {
        public override int Station => 1;
        protected static string CaseLink(int id) => Root + "/cases/" + Id(id);

        protected static async Task<AgentStepResult> CallAsync(IServiceProvider services, EnCaseActionDto dto, string doneSummary, string number, CancellationToken ct)
        {
            ResponseDto<EnCaseDto> r = await services.GetRequiredService<IEnCaseService>().CaseActionAsync(dto, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("EnCase", r.Data.Id, number ?? r.Data.Number, doneSummary);
        }
    }

    /// <summary>ENG.CASE.OPEN (Agent): a controlled inquiry (an open one; spare parts and service inquiries get a case of their own kind) or a controlled live contract with a work order and no engineering case.</summary>
    internal sealed class EnCaseOpenStep : EnCaseStep
    {
        public override string Code => "ENG.CASE.OPEN";
        public override string Title => "ایجنت مهندسی: باز کردن پروندهٔ مهندسی برای استعلام یا قرارداد";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            DateTimeOffset? start = await EnControl.StartAsync(uow, ct);
            List<AgentWorkItem> items = new List<AgentWorkItem>();
            if (!start.HasValue) return items;
            DateTimeOffset from = start.Value;
            var inquiries = await uow.Set<SlInquiry>().AsNoTracking().Where(i => !i.Deleted && i.CreateDate >= from
                    && (i.Status == SlInquiryStatus.Received || i.Status == SlInquiryStatus.UnderReview || i.Status == SlInquiryStatus.Bidding) && !uow.Set<EnCase>().Any(c => c.InquiryId == i.Id && !c.Deleted))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.Number }).ToListAsync(ct);
            items.AddRange(inquiries.Select(i => Item("SlInquiry", i.Id, i.Number, null, null, $"برای استعلام {i.Number} پروندهٔ مهندسی باز نشده است.")));
            var contracts = await ConFacts.Live(uow, today).Where(c => c.CreateDate >= from && uow.Set<WorkOrder>().Any(w => w.ContractId == c.Id && !w.Deleted)
                    && !uow.Set<EnCase>().Any(e => e.ContractId == c.Id && !e.Deleted)
                    && !uow.Set<SlProposal>().Any(p => p.ContractId == c.Id && !p.Deleted && uow.Set<EnCase>().Any(e => e.InquiryId == p.InquiryId && !e.Deleted)))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Code }).ToListAsync(ct);
            items.AddRange(contracts.Select(c => Item("ContractEntity", c.Id, c.Code, c.Id, null, $"برای قرارداد {c.Code} پروندهٔ مهندسی باز نشده است.")));
            return items;
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, item.TargetType == "SlInquiry" ? new EnCaseActionDto { Action = "open", InquiryId = item.TargetId } : new EnCaseActionDto { Action = "open", ContractId = item.TargetId }, $"ایجنت مهندسی برای {item.TargetNumber} پروندهٔ مهندسی باز کرد.", null, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = Root + "/cases", Title = "باز کردن پروندهٔ مهندسی " + item.TargetNumber };
    }

    /// <summary>ENG.CASE.STANDARD (Input): the standard of a project is set by the client in its data sheet; nobody in the company chooses it and the agent never assumes it. The task stays open while the case has none.</summary>
    internal sealed class EnCaseStandardStep : EnCaseStep
    {
        public override string Code => "ENG.CASE.STANDARD";
        public override string Title => "ثبت استاندارد پروژه از دیتاشیت کارفرما";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.StandardSetters;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && (c.ReferenceCode == null || c.ReferenceCode == "") && c.Status != EnCaseStatus.ClosedLost && c.Status != EnCaseStatus.Cancelled && c.Status != EnCaseStatus.Released)
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"استاندارد پروژهٔ {r.Number} را از دیتاشیت کارفرما ثبت کنید.", EnRoles.StandardSetters)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.StandardSetters, Link = CaseLink(item.TargetId), Title = "ثبت استاندارد پروژه " + item.TargetNumber };
    }

    /// <summary>ENG.CASE.PROPOSE (Agent): proposes platform (one product of the bank matches the equipment) or new design (none matches) with the evidence; several matches are left to an engineer. No model: a rule.</summary>
    internal sealed class EnCaseProposeStep : EnCaseStep
    {
        public override string Code => "ENG.CASE.PROPOSE";
        public override string Title => "ایجنت مهندسی: پیشنهاد نوع پرونده (پلتفرم یا طراحی جدید)";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.Classifying && c.ProposalBasis == null && c.ReturnReason == null && c.ProposedAt == null)
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"نوع پروندهٔ {r.Number} (پلتفرم یا طراحی جدید) هنوز پیشنهاد نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            EnCase c = await uow.Set<EnCase>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.TargetId && !x.Deleted, ct);
            if (c == null || c.Status != EnCaseStatus.Classifying) return AgentStepResult.Waiting("پرونده در حال تعیین نوع نیست.");
            List<int> equipments = new List<int>();
            string source;
            if (c.ContractId.HasValue)
            {
                equipments = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.ContractId == c.ContractId.Value && !w.Deleted).Select(w => w.EquipmentId).Distinct().ToListAsync(ct);
                source = "تجهیز دستورکارهای قرارداد";
            }
            else
            {
                equipments = await (from ee in uow.Set<SlEstimateEquipment>().AsNoTracking() join e in uow.Set<SlEstimate>().AsNoTracking() on ee.EstimateId equals e.Id
                                    where e.InquiryId == c.InquiryId.Value && !e.Deleted select ee.EquipmentId).Distinct().ToListAsync(ct);
                source = "تجهیز برآورد استعلام";
                if (equipments.Count == 0)
                {
                    var inquiry = await uow.Set<SlInquiry>().AsNoTracking().Where(i => i.Id == c.InquiryId.Value).Select(i => new { i.Id, i.ProductType, i.Quantity }).FirstAsync(ct);
                    var precedent = await SlPrecedent.ForAsync(uow, new List<SlPrecedent.Asked> { new SlPrecedent.Asked { Id = inquiry.Id, Type = inquiry.ProductType, Quantity = inquiry.Quantity } }, ct);
                    if (precedent.TryGetValue(inquiry.Id, out SlPrecedent.Precedent p)) { equipments = p.Equipments.Select(x => x.EquipmentId).Distinct().ToList(); source = "تجهیز استعلام برندهٔ قبلی از همین نوع"; }
                }
            }
            int checkedProducts = await uow.Set<PbProduct>().AsNoTracking().CountAsync(p => !p.Deleted && p.IsActive, ct);
            // only a product that has at least one dossier is a platform: the product of a new design is not one until the CEO approved its registration
            var matches = equipments.Count == 0 ? new List<(int Id, string Code)>() : (await uow.Set<PbProduct>().AsNoTracking().Where(p => !p.Deleted && p.IsActive && p.EquipmentId.HasValue && equipments.Contains(p.EquipmentId.Value) && uow.Set<EnPlatformDossier>().Any(d => d.ProductId == p.Id && !d.Deleted)).OrderBy(p => p.Id).Select(p => new { p.Id, p.Code }).Take(20).ToListAsync(ct)).Select(p => (p.Id, p.Code)).ToList();
            string number = c.Number;
            if (c.Kind != EnCaseKind.Turbine)
            {
                // spare parts and service: no classification; the related platform is proposed when it can be derived, the technical opinion is written by an engineer
                int? related = matches.Count == 1 ? matches[0].Id : (int?)null;
                string basisText = matches.Count == 1 ? $"{source} با پلتفرم {matches[0].Code} (دارای پرونده مدارک) برابر است؛ پلتفرم مرتبط پیشنهاد شد."
                    : matches.Count == 0 ? $"از {EnText2.Num(checkedProducts)} محصول فعال بانک محصول، هیچ پلتفرمِ دارای پرونده مدارک با {source} برابر نیست؛ پلتفرم مرتبط مشخص نشد."
                    : $"چند پلتفرم با {source} برابر است ({string.Join("، ", matches.Select(m => m.Code))}); مهندس پلتفرم مرتبط را انتخاب کند.";
                return await CallAsync(services, new EnCaseActionDto { Action = "propose", Id = c.Id, ProductId = related, Basis = basisText }, $"ایجنت مهندسی برای {number} " + (related.HasValue ? $"پلتفرم مرتبط {matches[0].Code} را پیشنهاد کرد." : "پلتفرم مرتبطی تعیین نکرد."), number, ct);
            }
            if (matches.Count == 1)
                return await CallAsync(services, new EnCaseActionDto { Action = "propose", Id = c.Id, Classification = (int)EnClassification.Platform, ProductId = matches[0].Id, Basis = $"{source} با محصول {matches[0].Code} در بانک محصول برابر است (از {EnText2.Num(checkedProducts)} محصول فعال تنها همین یکی)." }, $"ایجنت مهندسی برای {number} «پلتفرم {matches[0].Code}» را پیشنهاد کرد.", number, ct);
            if (matches.Count == 0)
                return await CallAsync(services, new EnCaseActionDto { Action = "propose", Id = c.Id, Classification = (int)EnClassification.New, Basis = $"هیچ‌یک از {EnText2.Num(checkedProducts)} محصول فعال بانک محصول که پرونده مدارک (پلتفرم) دارد با {source} ({EnText2.Num(equipments.Count)} تجهیز) برابر نیست." }, $"ایجنت مهندسی برای {number} «طراحی جدید» را پیشنهاد کرد.", number, ct);
            return await CallAsync(services, new EnCaseActionDto { Action = "propose", Id = c.Id, Classification = 0, Basis = $"چند محصول بانک با {source} برابر است ({string.Join("، ", matches.Select(m => m.Code))}); مهندس باید محصول را انتخاب کند یا طراحی جدید بداند." }, $"ایجنت مهندسی برای {number} چند محصول مطابق یافت و تصمیم را به مهندس سپرد.", number, ct);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "پیشنهاد نوع پروندهٔ مهندسی " + item.TargetNumber };
    }

    /// <summary>ENG.CASE.CLASSIFY.INPUT (Input): the agent could not decide (several products match) or the manager returned the proposal: an engineer picks platform and product, or new design, with a written basis.</summary>
    internal sealed class EnCaseClassifyInputStep : EnCaseStep
    {
        public override string Code => "ENG.CASE.CLASSIFY.INPUT";
        public override string Title => "تعیین نوع پرونده توسط مهندس (پلتفرم یا طراحی جدید)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.Classifying && c.ProposedAt == null && (c.ProposalBasis != null || c.ReturnReason != null))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId, c.ReturnReason }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, r.ReturnReason != null ? $"پروندهٔ {r.Number} برگشت خورد: {r.ReturnReason}" : $"پروندهٔ {r.Number}: ایجنت نتوانست نوع را تعیین کند؛ مهندس تصمیم می‌گیرد.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "تعیین نوع پروندهٔ مهندسی " + item.TargetNumber };
    }

    /// <summary>ENG.CASE.CONFIRM (HumanGate): the engineering manager confirms or changes the proposal (a manager who changed it does not confirm it) or returns it.</summary>
    internal sealed class EnCaseConfirmStep : EnCaseStep
    {
        public override string Code => "ENG.CASE.CONFIRM";
        public override string Title => "تایید نوع پرونده توسط مدیر مهندسی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.AwaitingEngConfirm).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId, c.ProposedClassification }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"پیشنهاد نوع پروندهٔ {r.Number} ({EnText.Classification(r.ProposedClassification)}) منتظر تایید مدیر مهندسی است.", EnRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = CaseLink(item.TargetId), Title = "تایید نوع پروندهٔ مهندسی " + item.TargetNumber };
    }

    /// <summary>ENG.OPINION.INPUT (Input): a spare-parts or service case has no classification; an engineer writes the technical opinion (min 30 characters) that the manager then confirms.</summary>
    internal sealed class EnOpinionInputStep : EnCaseStep
    {
        public override string Code => "ENG.OPINION.INPUT";
        public override string Title => "ثبت رای فنی مهندسی برای قطعات یدکی و خدمات پس از فروش";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Kind != EnCaseKind.Turbine && c.Status == EnCaseStatus.Classifying && c.OpinionText == null)
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"رای فنی مهندسی برای پروندهٔ {r.Number} (قطعات یدکی یا خدمات) هنوز ثبت نشده است.", EnRoles.Writers)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "ثبت رای فنی پروندهٔ " + item.TargetNumber };
    }

    /// <summary>ENG.CASE.APPROVE (HumanGate): the CEO approves the classification (not the confirmer, not the proposer or an editor).</summary>
    internal sealed class EnCaseApproveStep : EnCaseStep
    {
        public override string Code => "ENG.CASE.APPROVE";
        public override string Title => "تصویب نوع پرونده توسط مدیرعامل";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.AwaitingCeoApproval).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId, c.ProposedClassification, c.ReferenceCode }).ToListAsync(ct);
            HashSet<string> approved = await EnReferenceRules.ApprovedSetAsync(uow, ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"نوع پروندهٔ {r.Number} ({EnText.Classification(r.ProposedClassification)}) منتظر تصویب مدیرعامل است." + (string.IsNullOrWhiteSpace(r.ReferenceCode) || EnReferenceRules.IsApproved(approved, r.ReferenceCode) ? string.Empty : " تا تایید فهرست MDL استاندارد «" + r.ReferenceCode + "» توسط مدیر مهندسی، تایید مدیرعامل بسته است."), EnRoles.Ceo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Ceo, Link = CaseLink(item.TargetId), Title = "تصویب نوع پروندهٔ مهندسی " + item.TargetNumber };
    }

    // ------------------------------------------------------------------------------------------ the tender package

    /// <summary>ENG.TENDER.DRAFT (Agent): the package of an approved case whose inquiry is being bid; for a platform the tender-stage documents of the dossier are added as they become available (idempotent).</summary>
    internal sealed class EnTenderDraftStep : EnCaseStep
    {
        public override string Code => "ENG.TENDER.DRAFT";
        public override string Title => "ایجنت مهندسی: تهیهٔ پیش‌نویس بستهٔ فنی مناقصه";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var noPackage = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.TenderPreparation && c.TenderPackageId == null && uow.Set<SlInquiry>().Any(i => i.Id == c.InquiryId && i.Status == SlInquiryStatus.Bidding))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId }).ToListAsync(ct);
            var supplement = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.TenderPreparation && c.TenderPackageId != null && c.Classification == EnClassification.Platform && c.ProductId != null
                    && uow.Set<SlInquiry>().Any(i => i.Id == c.InquiryId && i.Status == SlInquiryStatus.Bidding)
                    && uow.Set<EnPlatformDocument>().Any(d => d.Dossier.ProductId == c.ProductId && d.Dossier.ReferenceCode == c.ReferenceCode && !d.Dossier.Deleted && d.TenderStage && d.State == EnLineState.Available && d.DocumentId != null
                        && !uow.Set<PcTenderDocument>().Any(t => t.PackageId == c.TenderPackageId && !t.Deleted && t.DocumentId == d.DocumentId)))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId }).ToListAsync(ct);
            return noPackage.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"بستهٔ فنی مناقصهٔ پروندهٔ {r.Number} هنوز ساخته نشده است."))
                .Concat(supplement.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"مدارک تازهٔ مرحلهٔ مناقصهٔ پلتفرم پروندهٔ {r.Number} هنوز به بسته اضافه نشده است."))).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<EnCaseDto> r = await services.GetRequiredService<IEnCaseService>().CaseActionAsync(new EnCaseActionDto { Action = "draft-tender", Id = item.TargetId }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("EnCase", r.Data.Id, r.Data.Number, $"ایجنت مهندسی بستهٔ فنی {r.Data.TenderPackageCode} را آماده کرد ({EnText2.Num(r.Data.TenderDocumentCount)} سند)." + (string.IsNullOrEmpty(r.Data.PlatformWarning) ? string.Empty : " " + r.Data.PlatformWarning));
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "تهیهٔ بستهٔ فنی مناقصه " + item.TargetNumber };
    }

    /// <summary>ENG.TENDER.INPUT (Input): a platform whose dossier lacks tender-stage documents (supply them in the dossier), or a new design without conceptual documents or concept note.</summary>
    internal sealed class EnTenderInputStep : EnCaseStep
    {
        public override string Code => "ENG.TENDER.INPUT";
        public override string Title => "تکمیل بستهٔ فنی مناقصه (مدارک را مهندسی دارد)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var platform = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.TenderPreparation && c.Classification == EnClassification.Platform && c.ProductId != null
                    && uow.Set<SlInquiry>().Any(i => i.Id == c.InquiryId && i.Status == SlInquiryStatus.Bidding)
                    && uow.Set<EnPlatformDocument>().Any(d => d.Dossier.ProductId == c.ProductId && d.Dossier.ReferenceCode == c.ReferenceCode && !d.Dossier.Deleted && d.TenderStage && d.State == EnLineState.Missing))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId }).ToListAsync(ct);
            var fresh = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.TenderPreparation && c.Classification == EnClassification.New
                    && uow.Set<SlInquiry>().Any(i => i.Id == c.InquiryId && i.Status == SlInquiryStatus.Bidding)
                    && (c.ConceptNote == null || c.TenderPackageId == null || !uow.Set<PcTenderDocument>().Any(t => t.PackageId == c.TenderPackageId && !t.Deleted)))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId }).ToListAsync(ct);
            return platform.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"پروندهٔ {r.Number}: مدارک مرحلهٔ مناقصه در پرونده پلتفرم موجود نیست؛ آن‌ها را در پرونده پلتفرم تامین کنید تا ایجنت به بسته بیفزاید."))
                .Concat(fresh.Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"پروندهٔ {r.Number} طراحی جدید است: اسناد طراحی مفهومی (فهرست اجزا و تعریف کافی برای قیمت‌گذاری) را به بستهٔ فنی بیفزایید و یادداشت طراحی مفهومی را بنویسید."))).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "تکمیل بستهٔ فنی مناقصه " + item.TargetNumber };
    }

    /// <summary>ENG.TENDER.REVALIDATE (Agent): a released package whose documents changed after the release: the release is void and the status goes back (commercial is told).</summary>
    internal sealed class EnTenderRevalidateStep : EnCaseStep
    {
        public override string Code => "ENG.TENDER.REVALIDATE";
        public override string Title => "ایجنت مهندسی: بازبینی بستهٔ فنی ابلاغ‌شده (تغییر پس از ابلاغ)";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.TenderIssued && c.TenderPackageId != null).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number, c.ContractId, c.TenderPackageId, c.TenderHash, c.Kind, c.OpinionText }).ToListAsync(ct);
            List<AgentWorkItem> items = new List<AgentWorkItem>();
            foreach (var r in rows)
                if (r.TenderHash != await EnCaseRules.TenderHashAsync(uow, r.TenderPackageId.Value, r.Kind, r.OpinionText, ct))
                    items.Add(Item("EnCase", r.Id, r.Number, r.ContractId, null, $"مدارک بستهٔ فنی پروندهٔ {r.Number} پس از ابلاغ تغییر کرده است."));
            return items;
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, new EnCaseActionDto { Action = "revalidate-tender", Id = item.TargetId }, $"ایجنت مهندسی ابلاغ بستهٔ فنی {item.TargetNumber} را باطل کرد؛ مدارک پس از ابلاغ تغییر کرده بود.", item.TargetNumber, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "بازبینی بستهٔ فنی " + item.TargetNumber };
    }

    /// <summary>ENG.TENDER.RELEASE (HumanGate): a manager who did not make the package releases it to commercial.</summary>
    internal sealed class EnTenderReleaseStep : EnCaseStep
    {
        public override string Code => "ENG.TENDER.RELEASE";
        public override string Title => "ابلاغ بستهٔ فنی مناقصه به بازرگانی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.Status == EnCaseStatus.TenderPreparation && c.TenderPackageId != null && uow.Set<SlInquiry>().Any(i => i.Id == c.InquiryId && i.Status == SlInquiryStatus.Bidding)
                    && (c.Classification == EnClassification.Platform || (c.Kind != EnCaseKind.Turbine && c.OpinionText != null) || (c.ConceptNote != null && uow.Set<PcTenderDocument>().Any(t => t.PackageId == c.TenderPackageId && !t.Deleted))))
                .OrderBy(c => c.Id).ToListAsync(ct);
            // employer rule 20 (every kind of tender): the gate opens only when the release is possible as far as the project MDL is concerned (an approved list whose hash holds, or the CEO's permit): the same check the release action runs,
            // so the 72 office hour alarm names the work that is really waiting (the MDL check, the approval or the permit have their own gates)
            List<AgentWorkItem> items = new List<AgentWorkItem>();
            foreach (EnCase r in rows)
            {
                if (items.Count >= ProcStepCommon.Cap) break;
                if ((await EnProjectMdlRules.ReleaseLockAsync(uow, r, ct)).Refusal != null) continue;
                items.Add(Item("EnCase", r.Id, r.Number, r.ContractId, null, $"بستهٔ فنی پروندهٔ {r.Number} آمادهٔ ابلاغ به بازرگانی است.", EnRoles.Manager));
            }
            return items;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = CaseLink(item.TargetId), Title = "ابلاغ بستهٔ فنی مناقصه " + item.TargetNumber };
    }

    // ------------------------------------------------------------------------------------------ the result of the inquiry

    /// <summary>ENG.CASE.AWARD (Agent): the inquiry is won and its proposal made the contract: the case gets the contract and becomes Awarded.</summary>
    internal sealed class EnCaseAwardStep : EnCaseStep
    {
        public override string Code => "ENG.CASE.AWARD";
        public override string Title => "ایجنت مهندسی: ثبت برنده‌شدن و اتصال قرارداد به پروندهٔ مهندسی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.InquiryId != null && (c.Status == EnCaseStatus.TenderPreparation || c.Status == EnCaseStatus.TenderIssued)
                    && uow.Set<SlInquiry>().Any(i => i.Id == c.InquiryId && i.Status == SlInquiryStatus.Won)
                    && uow.Set<SlProposal>().Any(p => p.InquiryId == c.InquiryId && !p.Deleted && p.ContractId != null))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, null, null, $"استعلام پروندهٔ {r.Number} برنده شده و قرارداد ساخته شده است.")).ToList();
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, new EnCaseActionDto { Action = "award", Id = item.TargetId }, $"ایجنت مهندسی قرارداد را به پروندهٔ {item.TargetNumber} وصل کرد.", item.TargetNumber, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "ثبت برنده‌شدن پروندهٔ مهندسی " + item.TargetNumber };
    }

    /// <summary>ENG.CASE.CLOSE (Agent): the inquiry was lost, not bid or cancelled: the case closes with the reason of the inquiry.</summary>
    internal sealed class EnCaseCloseStep : EnCaseStep
    {
        public override string Code => "ENG.CASE.CLOSE";
        public override string Title => "ایجنت مهندسی: بستن پروندهٔ مهندسی با نتیجهٔ استعلام";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => EnRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnCase>().AsNoTracking().Where(c => !c.Deleted && c.InquiryId != null && c.Status < EnCaseStatus.Awarded
                    && uow.Set<SlInquiry>().Any(i => i.Id == c.InquiryId && (i.Status == SlInquiryStatus.Lost || i.Status == SlInquiryStatus.NoBid || i.Status == SlInquiryStatus.Cancelled)))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).Select(c => new { c.Id, c.Number }).ToListAsync(ct);
            return rows.Select(r => Item("EnCase", r.Id, r.Number, null, null, $"استعلام پروندهٔ {r.Number} بازنده یا لغو شده است.")).ToList();
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            CallAsync(services, new EnCaseActionDto { Action = "close", Id = item.TargetId }, $"ایجنت مهندسی پروندهٔ {item.TargetNumber} را با نتیجهٔ استعلام بست.", item.TargetNumber, ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = CaseLink(item.TargetId), Title = "بستن پروندهٔ مهندسی " + item.TargetNumber };
    }

    // ------------------------------------------------------------------------------------------ clarifications

    internal abstract class EnClarStep : EnCaseStep
    {
        protected static string ClarLink(int id) => Root + "/clarifications/" + Id(id);
    }

    /// <summary>ENG.CLAR.SEND (Physical): commercial sends the question to the client in a letter and records its number.</summary>
    internal sealed class EnClarSendStep : EnClarStep
    {
        public override string Code => "ENG.CLAR.SEND";
        public override string Title => "ارسال پرسش مهندسی به کارفرما (توسط بازرگانی)";
        public override AgentStepKind Kind => AgentStepKind.Physical;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnClarification>().AsNoTracking().Where(x => !x.Deleted && x.Status == EnClarStatus.WithCommercial).OrderBy(x => x.Id).Take(ProcStepCommon.Cap).Select(x => new { x.Id, x.Number, ContractId = uow.Set<EnCase>().Where(c => c.Id == x.CaseId).Select(c => c.ContractId).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnClarification", r.Id, r.Number, r.ContractId, null, $"پرسش مهندسی {r.Number} نزد بازرگانی است و باید برای کارفرما فرستاده شود.", EnRoles.Commercial)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Commercial, Link = ClarLink(item.TargetId), Title = "ارسال پرسش مهندسی " + item.TargetNumber + " به کارفرما" };
    }

    /// <summary>ENG.CLAR.ANSWER (Input): commercial records the answer of the client (text or file).</summary>
    internal sealed class EnClarAnswerStep : EnClarStep
    {
        public override string Code => "ENG.CLAR.ANSWER";
        public override string Title => "ثبت پاسخ کارفرما به پرسش مهندسی (توسط بازرگانی)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnClarification>().AsNoTracking().Where(x => !x.Deleted && x.Status == EnClarStatus.SentToClient).OrderBy(x => x.Id).Take(ProcStepCommon.Cap).Select(x => new { x.Id, x.Number, ContractId = uow.Set<EnCase>().Where(c => c.Id == x.CaseId).Select(c => c.ContractId).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnClarification", r.Id, r.Number, r.ContractId, null, $"پرسش مهندسی {r.Number} برای کارفرما فرستاده شده و پاسخ آن ثبت نشده است.", EnRoles.Commercial)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Commercial, Link = ClarLink(item.TargetId), Title = "ثبت پاسخ کارفرما به پرسش " + item.TargetNumber };
    }

    /// <summary>ENG.CLAR.CLOSE (HumanGate): engineering accepts the answer (close) or asks again.</summary>
    internal sealed class EnClarCloseStep : EnClarStep
    {
        public override string Code => "ENG.CLAR.CLOSE";
        public override string Title => "بررسی پاسخ کارفرما توسط مهندسی (پذیرش یا پرسش دوباره)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<EnClarification>().AsNoTracking().Where(x => !x.Deleted && x.Status == EnClarStatus.Answered).OrderBy(x => x.Id).Take(ProcStepCommon.Cap).Select(x => new { x.Id, x.Number, ContractId = uow.Set<EnCase>().Where(c => c.Id == x.CaseId).Select(c => c.ContractId).FirstOrDefault() }).ToListAsync(ct);
            return rows.Select(r => Item("EnClarification", r.Id, r.Number, r.ContractId, null, $"کارفرما به پرسش مهندسی {r.Number} پاسخ داده است؛ مهندسی آن را می‌پذیرد یا دوباره می‌پرسد.", EnRoles.Writers)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Writers, Link = ClarLink(item.TargetId), Title = "بررسی پاسخ کارفرما به پرسش " + item.TargetNumber };
    }

    // ------------------------------------------------------------------------------------------ the charter (station 3)

    /// <summary>ENG.CHARTER.REVIEW (HumanGate): the engineering manager confirms the content of the project charter (or returns it) before the CEO approves it.</summary>
    internal sealed class EnCharterReviewStep : EnCaseStep
    {
        public override string Code => "ENG.CHARTER.REVIEW";
        public override int Station => 3;
        public override string Title => "تایید منشور پروژه توسط مهندسی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;
        public override string FallbackRoles => null;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await (from c in uow.Set<EnCase>().AsNoTracking()
                              join ch in uow.Set<PcProjectCharter>().AsNoTracking() on c.ContractId equals (int?)ch.ContractId
                              where !c.Deleted && !ch.Deleted && (c.Status == EnCaseStatus.Awarded || c.Status == EnCaseStatus.InEngineering) && ch.ManagerAcceptedAt != null && ch.ApprovedAt == null
                              orderby c.Id select new { c.Id, c.Number, c.ContractId, c.CharterHash, c.CharterReviewOutcome, ch }).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Where(r => r.CharterHash != EnCaseRules.CharterHash(r.ch)).Select(r => Item("EnCase", r.Id, r.Number, r.ContractId, null, $"منشور پروژهٔ پروندهٔ {r.Number} پذیرفته شده و محتوای فعلی آن هنوز توسط مهندسی تایید نشده است.", EnRoles.Manager)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = EnRoles.Manager, Link = CaseLink(item.TargetId), Title = "تایید منشور پروژه توسط مهندسی " + item.TargetNumber };
    }
}
