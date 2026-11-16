using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Inventory;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Stations 2 (contract review) and 3 (project initiation). The contract agent drafts the commercial terms and the review checklist from the contract record, its terms and its equipment, and a person only
    // decides at the gates (capability of planning, the two signatures of the review, the project manager and the charter, the PEP) or does the physical work (the kick-off meeting with the client).
    // What exists only on the signed paper contract (the percentages of advance, retention and penalty) is the one input of a person.

    public static class ContractInitSteps
    {
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddContractInitSteps(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            services.AddScoped<IAgentStep, TermsDraftStep>();
            services.AddScoped<IAgentStep, TermsInputStep>();
            services.AddScoped<IAgentStep, ReviewDraftStep>();
            services.AddScoped<IAgentStep, ReviewCompleteStep>();
            services.AddScoped<IAgentStep, ReviewCapabilityStep>();
            services.AddScoped<IAgentStep, ReviewAcceptStep>();
            services.AddScoped<IAgentStep, InitStartStep>();
            services.AddScoped<IAgentStep, InitCodeStep>();
            services.AddScoped<IAgentStep, InitCodeInputStep>();
            services.AddScoped<IAgentStep, InitUnitsStep>();
            services.AddScoped<IAgentStep, InitManagerStep>();
            services.AddScoped<IAgentStep, CharterDraftStep>();
            services.AddScoped<IAgentStep, CharterAcceptStep>();
            services.AddScoped<IAgentStep, CharterApproveStep>();
            services.AddScoped<IAgentStep, PepDraftStep>();
            services.AddScoped<IAgentStep, PepApproveStep>();
            services.AddScoped<IAgentStep, KickOffStep>();
            services.AddScoped<IAgentStep, BomRequestStep>();
            return services;
        }
    }

    /// <summary>The facts of a contract that the steps of stations 2 and 3 share (all read only).</summary>
    internal static class ConFacts
    {
        /// <summary>A contract older than this (days since its date) is not started by the agents: its initiation, if any, was done by hand.</summary>
        public const int LiveDays = 400;

        private static readonly DateTimeOffset RealFrom = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static IQueryable<ContractEntity> Live(IUnitOfWork uow, DateTime today)
        {
            DateTimeOffset since = new DateTimeOffset(DateTime.SpecifyKind(today.Date.AddDays(-LiveDays), DateTimeKind.Utc));
            return uow.Set<ContractEntity>().AsNoTracking().Where(c => !c.Deleted && c.Status == ContractStatus.Current && c.ContractDate >= since);
        }

        public static IQueryable<int> AcceptedContracts(IUnitOfWork uow) =>
            uow.Set<PcContractReview>().AsNoTracking().Where(r => !r.Deleted && r.Stage == PcReviewStage.Contract && r.Decision == PcReviewDecision.Accept && r.ContractId != null).Select(r => r.ContractId.Value);

        public static IQueryable<int> ReviewedContracts(IUnitOfWork uow) =>
            uow.Set<PcContractReview>().AsNoTracking().Where(r => !r.Deleted && r.Stage == PcReviewStage.Contract && r.ContractId != null).Select(r => r.ContractId.Value);

        public static IQueryable<int> StepPending(IUnitOfWork uow, PcInitiationStep step) =>
            uow.Set<PcInitiationStepRecord>().AsNoTracking().Where(s => !s.Initiation.Deleted && s.Step == step && s.DoneAt == null).Select(s => s.Initiation.ContractId);

        public static IQueryable<int> StepDone(IUnitOfWork uow, PcInitiationStep step) =>
            uow.Set<PcInitiationStepRecord>().AsNoTracking().Where(s => !s.Initiation.Deleted && s.Step == step && s.DoneAt != null).Select(s => s.Initiation.ContractId);

        public static DateTime? Real(DateTimeOffset d) => d >= RealFrom ? d.Date : (DateTime?)null;

        /// <summary>The delivery date that the contract record implies: the delivery date field, else the start date plus the duration in days.</summary>
        public static DateTime? Delivery(ContractEntity c)
        {
            DateTime? delivery = Real(c.DeliverDate);
            if (delivery.HasValue) return delivery;
            DateTime? start = Real(c.StartDate);
            return start.HasValue && c.Duration > 0 ? start.Value.AddDays(c.Duration.Value) : (DateTime?)null;
        }

        public static string Link(int contractId, string tab, int? review = null) =>
            "/project-control/project?contract=" + contractId.ToString(CultureInfo.InvariantCulture) + "&tab=" + tab + (review.HasValue ? "&review=" + review.Value.ToString(CultureInfo.InvariantCulture) : "");

        public static string Date(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "نامشخص";

        public static string Money(decimal? v) => v.HasValue ? v.Value.ToString("N0", CultureInfo.InvariantCulture) : "نامشخص";

        public static string Pct(decimal? v) => v.HasValue ? v.Value.ToString("0.##", CultureInfo.InvariantCulture) + "٪" : "نامشخص";

        public static async Task<List<(string Title, int Count)>> EquipmentLinesAsync(IUnitOfWork uow, int contractId, CancellationToken ct)
        {
            var rows = await uow.Set<ContractEquipment>().AsNoTracking().Where(e => e.ContractId == contractId).OrderBy(e => e.EquipmentId)
                .Select(e => new { e.Equipment.Code, e.Equipment.Title, e.Count }).ToListAsync(ct);
            return rows.Select(r => ((string.IsNullOrWhiteSpace(r.Code) ? r.Title : r.Code + " - " + r.Title) ?? "تجهیز", r.Count)).ToList();
        }

        public static async Task<PcCategory?> CategoryAsync(IUnitOfWork uow, ContractEntity c, CancellationToken ct)
        {
            string text = ((c.Type ?? "") + " " + (c.Title ?? "")).ToLowerInvariant();
            if (text.Contains("یدکی") || text.Contains("spare")) return PcCategory.SpareParts;
            if (await uow.Set<ContractEquipment>().AsNoTracking().AnyAsync(e => e.ContractId == c.Id, ct)) return PcCategory.Manufacturing;
            if (!string.IsNullOrWhiteSpace(c.Services)) return PcCategory.Services;
            return null;
        }

        public static AgentWorkItem Item(ContractEntity c, string type, int id, string summary, string roles = null) =>
            new AgentWorkItem { TargetType = type, TargetId = id, TargetNumber = c.Code, ContractId = c.Id, Summary = summary, GateRoles = roles };

        public static AgentWorkItem ContractItem(ContractEntity c, string summary, string roles = null) => Item(c, "Contract", c.Id, summary, roles);
    }

    /// <summary>The base of the steps of stations 2 and 3.</summary>
    internal abstract class ConStep : ProcStep
    {
        public abstract int StationNo { get; }
        public sealed override int Station => StationNo;

        protected static IPcProjectService Pc(IServiceProvider sp) => sp.GetRequiredService<IPcProjectService>();

        protected static async Task<ContractEntity> ContractAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
            await uow.Set<ContractEntity>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && !c.Deleted, ct);
    }

    // ================================================================================================================================ station 2

    /// <summary>CON.TERMS.DRAFT: the agent writes the commercial terms that the contract record already holds (amount, delivery date, schedule start, currency).</summary>
    internal sealed class TermsDraftStep : ConStep
    {
        public override string Code => "CON.TERMS.DRAFT";
        public override int StationNo => 2;
        public override string AgentCode => AgentCodes.Contract;
        public override string Title => "ایجنت قرارداد: ثبت شرایط تجاری از روی اطلاعات قرارداد";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PMO,COMMERCIAL";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> accepted = ConFacts.AcceptedContracts(uow);
            IQueryable<PcContractTerms> terms = uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted);
            var rows = await ConFacts.Live(uow, today).Where(c => !accepted.Contains(c.Id) && c.Price > 0
                    && !terms.Any(t => t.ContractId == c.Id && t.ValueExVat > 0 && t.ContractDeliveryDate != null && t.CurrencyCode != null && t.CurrencyCode != ""))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            // a contract whose delivery date the record cannot give is for a person (CON.TERMS.INPUT)
            return rows.Where(c => ConFacts.Delivery(c).HasValue).Select(c => ConFacts.ContractItem(c, "شرایط تجاری قرارداد (مبلغ، تاریخ تحویل، ارز) از اطلاعات قرارداد ثبت نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            ContractEntity c = await ContractAsync(uow, item.TargetId, ct);
            if (c == null) return AgentStepResult.Waiting("قرارداد پیدا نشد.");
            DateTime? delivery = ConFacts.Delivery(c);
            if (!(c.Price > 0) || !delivery.HasValue) return AgentStepResult.Waiting("مبلغ یا تاریخ تحویل در اطلاعات قرارداد نیست.");
            IPcProjectService pc = Pc(services);
            ResponseDto<PcContractTermsDto> got = await pc.GetTermsAsync(c.Id, ct);
            if (!got.Success) return ProcStepCommon.Refusal(got);
            PcContractTermsDto dto = got.Data;
            bool fresh = dto.RowVersion == null && dto.ValueExVat == null && dto.ProjectCode == null;
            if (!(dto.ValueExVat > 0)) dto.ValueExVat = c.Price;
            if (string.IsNullOrWhiteSpace(dto.CurrencyCode)) dto.CurrencyCode = "IRR";
            if (!dto.ContractDeliveryDate.HasValue) dto.ContractDeliveryDate = delivery;
            if (!dto.ScheduleStartDate.HasValue) dto.ScheduleStartDate = ConFacts.Real(c.StartDate);
            if (fresh)
            {
                // the defaults of a new terms record (the DTO of a contract without terms carries zeros)
                dto.PenaltyBasis = PcPenaltyBasis.PerContract; dto.DataDateWeekday = 3; dto.ClientReviewDays = 14;
                dto.WarrantyMonthsAfterCommissioning = 12; dto.WarrantyMonthsAfterDelivery = 18; dto.AutoSnapshot = true;
            }
            ResponseDto<PcContractTermsDto> saved = await pc.SaveTermsAsync(dto, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("Contract", c.Id, c.Code, $"ایجنت شرایط قرارداد {c.Code} را از اطلاعات قرارداد ثبت کرد: مبلغ {ConFacts.Money(dto.ValueExVat)}، تحویل {ConFacts.Date(dto.ContractDeliveryDate)}، ارز {dto.CurrencyCode}.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = ConFacts.Link(item.TargetId, "terms"), Title = "شرایط تجاری قرارداد " + item.TargetNumber };
    }

    /// <summary>CON.TERMS.INPUT (Input): what exists only on the signed paper contract, the percentages of advance, retention and penalty, and the amount or date that the record lacks.</summary>
    internal sealed class TermsInputStep : ConStep
    {
        public override string Code => "CON.TERMS.INPUT";
        public override int StationNo => 2;
        public override string AgentCode => AgentCodes.Contract;
        public override string Title => "ثبت درصدهای پیش‌پرداخت، حسن انجام کار و جریمه از متن قرارداد امضاشده";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> accepted = ConFacts.AcceptedContracts(uow);
            List<ContractEntity> contracts = await ConFacts.Live(uow, today).Where(c => !accepted.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            int[] ids = contracts.Select(c => c.Id).ToArray();
            Dictionary<int, PcContractTerms> terms = (await uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && ids.Contains(t.ContractId)).ToListAsync(ct)).GroupBy(t => t.ContractId).ToDictionary(g => g.Key, g => g.First());
            var items = new List<AgentWorkItem>();
            foreach (ContractEntity c in contracts)
            {
                terms.TryGetValue(c.Id, out PcContractTerms t);
                var missing = new List<string>();
                if (!(t?.ValueExVat > 0) && !(c.Price > 0)) missing.Add("مبلغ قرارداد");
                if (t?.ContractDeliveryDate == null && !ConFacts.Delivery(c).HasValue) missing.Add("تاریخ تحویل قرارداد");
                if (t?.AdvancePercent == null) missing.Add("درصد پیش‌پرداخت");
                if (t?.RetentionPercent == null) missing.Add("درصد حسن انجام کار");
                if (t?.LatePenaltyPercentPerWeek == null) missing.Add("درصد جریمه تاخیر در هفته");
                if (t?.PenaltyCapPercent == null) missing.Add("سقف جریمه");
                if (missing.Count == 0) continue;
                items.Add(ConFacts.ContractItem(c, "این موارد فقط در متن قرارداد امضاشده است و در سیستم نیست: " + string.Join("، ", missing) + ". پس از ثبت، ایجنت بازنگری قرارداد را پیش‌نویس می‌کند."));
            }
            return items;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "PMO,COMMERCIAL", Link = ConFacts.Link(item.TargetId, "terms"), Title = "ثبت شرایط قرارداد " + item.TargetNumber };
    }

    /// <summary>CON.REVIEW.DRAFT: the review checklist of the contract with every answer that the contract, its terms and its equipment give.</summary>
    internal sealed class ReviewDraftStep : ConStep
    {
        public override string Code => "CON.REVIEW.DRAFT";
        public override int StationNo => 2;
        public override string AgentCode => AgentCodes.Contract;
        public override string Title => "ایجنت قرارداد: پیش‌نویس بازنگری قرارداد با پاسخ‌های برگرفته از قرارداد";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PMO,COMMERCIAL";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> reviewed = ConFacts.ReviewedContracts(uow);
            IQueryable<PcContractTerms> terms = uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted);
            // the terms are complete (the percentages came from the paper contract): the draft then leaves only the judgement items
            var rows = await ConFacts.Live(uow, today).Where(c => !reviewed.Contains(c.Id) && terms.Any(t => t.ContractId == c.Id && t.ValueExVat > 0 && t.ContractDeliveryDate != null && t.CurrencyCode != null && t.CurrencyCode != ""
                        && t.AdvancePercent != null && t.RetentionPercent != null && t.LatePenaltyPercentPerWeek != null && t.PenaltyCapPercent != null))
                .OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, "بازنگری قرارداد هنوز پیش‌نویس نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            ContractEntity c = await ContractAsync(uow, item.TargetId, ct);
            if (c == null) return AgentStepResult.Waiting("قرارداد پیدا نشد.");
            PcContractTerms t = await uow.Set<PcContractTerms>().AsNoTracking().FirstOrDefaultAsync(x => x.ContractId == c.Id && !x.Deleted, ct);
            if (t == null || !(t.ValueExVat > 0)) return AgentStepResult.Waiting("شرایط قرارداد با مبلغ آن ثبت نشده است.");
            if (await uow.Set<PcContractReview>().AsNoTracking().AnyAsync(r => r.ContractId == c.Id && !r.Deleted && r.Stage == PcReviewStage.Contract, ct)) return AgentStepResult.Waiting("برای این قرارداد بازنگری ثبت شده است.");

            List<(string Title, int Count)> equipment = await ConFacts.EquipmentLinesAsync(uow, c.Id, ct);
            List<int> equipmentIds = await uow.Set<ContractEquipment>().AsNoTracking().Where(e => e.ContractId == c.Id).Select(e => e.EquipmentId).Distinct().ToListAsync(ct);
            IQueryable<int> accepted = ConFacts.AcceptedContracts(uow);
            List<int> known = await uow.Set<ContractEquipment>().AsNoTracking().Where(e => equipmentIds.Contains(e.EquipmentId) && e.ContractId != c.Id && accepted.Contains(e.ContractId))
                .Select(e => e.EquipmentId).Distinct().ToListAsync(ct);
            bool guarantees = await uow.Set<ContractGuaranty>().AsNoTracking().AnyAsync(g => g.ContractEntityId == c.Id && !g.Deleted, ct);
            DateTime? start = t.ScheduleStartDate ?? ConFacts.Real(c.StartDate);

            bool standard = equipmentIds.Count > 0 && equipmentIds.All(known.Contains);
            bool requirements = !string.IsNullOrWhiteSpace(c.Title) && (equipmentIds.Count > 0 || !string.IsNullOrWhiteSpace(c.Services));
            bool differences = !(c.Price > 0) || c.Price == t.ValueExVat;
            bool delivery = t.ContractDeliveryDate.HasValue && (!start.HasValue || t.ContractDeliveryDate.Value > start.Value);
            bool penalties = t.LatePenaltyPercentPerWeek.HasValue && t.PenaltyCapPercent.HasValue;
            bool payment = t.AdvancePercent.HasValue && t.RetentionPercent.HasValue;
            bool currency = !string.IsNullOrWhiteSpace(t.CurrencyCode);

            var left = new List<string>();
            if (!requirements) left.Add("الزامات (عنوان یا فهرست تجهیز قرارداد کامل نیست)");
            if (!differences) left.Add("اختلاف مبلغ قرارداد با شرایط ثبت‌شده");
            if (!delivery) left.Add("امکان تحویل در موعد (تاریخ تحویل پس از شروع نیست)");
            if (!standard) left.Add("استانداردها و ITP (تجهیزی بدون سابقه قرارداد پذیرفته‌شده)");
            if (!guarantees) left.Add("ضمانت‌نامه‌ها (ضمانت‌نامه‌ای در قرارداد ثبت نشده)");
            string notes = "پیش‌نویس ایجنت از روی اطلاعات قرارداد، شرایط ثبت‌شده و تجهیزات: "
                + $"مبلغ {ConFacts.Money(t.ValueExVat)} {t.CurrencyCode}، تحویل {ConFacts.Date(t.ContractDeliveryDate)}، پیش‌پرداخت {ConFacts.Pct(t.AdvancePercent)}، حسن انجام کار {ConFacts.Pct(t.RetentionPercent)}، "
                + $"جریمه {ConFacts.Pct(t.LatePenaltyPercentPerWeek)} در هفته تا سقف {ConFacts.Pct(t.PenaltyCapPercent)}؛ {equipment.Count} قلم تجهیز"
                + (equipment.Count > 0 ? " (" + string.Join("، ", equipment.Take(6).Select(e => e.Title + " × " + e.Count)) + ")" : "") + "؛ نوع: " + (standard ? "محصول استاندارد (همه تجهیزها در قراردادهای پذیرفته‌شده قبلی بوده‌اند)" : "سفارش ویژه")
                + (left.Count > 0 ? ". برای تصمیم انسان مانده: " + string.Join("، ", left) : ". همه پاسخ‌ها از قرارداد به دست آمد.");

            var dto = new PcReviewDto
            {
                ContractId = c.Id, Kind = standard ? PcReviewKind.StandardProduct : PcReviewKind.SpecialOrder, RequirementsDefined = requirements, DifferencesResolved = differences,
                DeliveryFeasible = delivery, StandardsItpAgreed = standard, GuaranteesReviewed = guarantees, PenaltiesReviewed = penalties, PaymentTermsReviewed = payment, CurrencyReviewed = currency, Notes = notes,
            };
            ResponseDto<PcReviewDto> saved = await Pc(services).SaveReviewAsync(dto, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PcContractReview", saved.Data.Id, c.Code, $"ایجنت بازنگری قرارداد {c.Code} را پیش‌نویس کرد؛ " + (left.Count > 0 ? left.Count + " مورد برای انسان مانده است." : "همه پاسخ‌ها از قرارداد به دست آمد."));
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = ConFacts.Link(item.TargetId, "reviews"), Title = "بازنگری قرارداد " + item.TargetNumber };
    }

    internal abstract class ReviewGateBase : ConStep
    {
        public override int StationNo => 2;
        public override string AgentCode => AgentCodes.Contract;

        protected static IQueryable<PcContractReview> Open(IUnitOfWork uow) =>
            uow.Set<PcContractReview>().AsNoTracking().Where(r => !r.Deleted && r.Stage == PcReviewStage.Contract && r.ContractId != null && (r.Decision == PcReviewDecision.Pending || r.Decision == PcReviewDecision.ReviewRequired));

        protected static async Task<Dictionary<int, ContractEntity>> ContractsAsync(IUnitOfWork uow, IEnumerable<int> ids, CancellationToken ct)
        {
            int[] list = ids.Distinct().ToArray();
            return (await uow.Set<ContractEntity>().AsNoTracking().Where(c => list.Contains(c.Id) && !c.Deleted && c.Status == ContractStatus.Current).ToListAsync(ct)).ToDictionary(c => c.Id);
        }
    }

    /// <summary>CON.REVIEW.COMPLETE (Input): the answers the contract does not give. Whoever ticks them has edited the review and does not sign it.</summary>
    internal sealed class ReviewCompleteStep : ReviewGateBase
    {
        public override string Code => "CON.REVIEW.COMPLETE";
        public override string Title => "تکمیل پاسخ‌های بازنگری قرارداد که از قرارداد به دست نیامد";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var reviews = await Open(uow).Where(r => !(r.RequirementsDefined && r.DifferencesResolved && r.DeliveryFeasible && r.StandardsItpAgreed && r.GuaranteesReviewed && r.PenaltiesReviewed && r.PaymentTermsReviewed && r.CurrencyReviewed))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, ContractEntity> contracts = await ContractsAsync(uow, reviews.Select(r => r.ContractId.Value), ct);
            return reviews.Where(r => contracts.ContainsKey(r.ContractId.Value)).Select(r =>
            {
                List<string> missing = PcGates.MissingItems(r).Where(m => !m.StartsWith("تایید توانایی", StringComparison.Ordinal)).ToList();
                return ConFacts.Item(contracts[r.ContractId.Value], "PcContractReview", r.Id, "پاسخ‌هایی که از قرارداد به دست نیامد و داوری انسان می‌خواهد: " + string.Join("، ", missing)
                    + ". کسی که این موارد را ثبت کند بازنگری را امضا نمی‌کند.");
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "COMMERCIAL,PMO,PLANNING", Link = ConFacts.Link(item.ContractId ?? 0, "reviews", item.TargetId), Title = "تکمیل بازنگری قرارداد " + item.TargetNumber };
    }

    /// <summary>CON.REVIEW.CAPABILITY (gate): planning confirms that the company can make a special order.</summary>
    internal sealed class ReviewCapabilityStep : ReviewGateBase
    {
        public override string Code => "CON.REVIEW.CAPABILITY";
        public override string Title => "تایید توانایی اجرای سفارش ویژه توسط برنامه‌ریزی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var reviews = await Open(uow).Where(r => r.Kind == PcReviewKind.SpecialOrder && !r.CapabilityChecked).OrderBy(r => r.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, ContractEntity> contracts = await ContractsAsync(uow, reviews.Select(r => r.ContractId.Value), ct);
            return reviews.Where(r => contracts.ContainsKey(r.ContractId.Value)).Select(r => ConFacts.Item(contracts[r.ContractId.Value], "PcContractReview", r.Id, "سفارش ویژه است؛ برنامه‌ریزی توانایی اجرا را تایید کند.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = "PLANNING", Link = ConFacts.Link(item.ContractId ?? 0, "reviews", item.TargetId), Title = "تایید توانایی اجرا " + item.TargetNumber };
    }

    /// <summary>CON.REVIEW.ACCEPT (gate): two different people sign the complete checklist, the PMO deputy and the commercial unit.</summary>
    internal sealed class ReviewAcceptStep : ReviewGateBase
    {
        public override string Code => "CON.REVIEW.ACCEPT";
        public override string Title => "پذیرش بازنگری قرارداد (امضای PMO و بازرگانی)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var reviews = await Open(uow).Where(r => r.RequirementsDefined && r.DifferencesResolved && r.DeliveryFeasible && r.StandardsItpAgreed && r.GuaranteesReviewed && r.PenaltiesReviewed && r.PaymentTermsReviewed && r.CurrencyReviewed
                    && (r.Kind == PcReviewKind.StandardProduct || r.CapabilityChecked) && (r.PmoAcceptedByUserId == null || r.CommercialAcceptedByUserId == null))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            Dictionary<int, ContractEntity> contracts = await ContractsAsync(uow, reviews.Select(r => r.ContractId.Value), ct);
            return reviews.Where(r => contracts.ContainsKey(r.ContractId.Value)).Select(r =>
            {
                // the same rule as ReviewActionAsync: each slot is signed by its own role, never by the one who saved or edited the review
                string roles = ProcStepCommon.Roles(r.PmoAcceptedByUserId == null ? "PMO" : null, r.CommercialAcceptedByUserId == null ? "COMMERCIAL" : null);
                return ConFacts.Item(contracts[r.ContractId.Value], "PcContractReview", r.Id, "بازنگری کامل است و منتظر امضای " + (r.PmoAcceptedByUserId == null && r.CommercialAcceptedByUserId == null ? "PMO و بازرگانی (دو نفر متفاوت)" : r.PmoAcceptedByUserId == null ? "PMO" : "بازرگانی") + " است.", roles);
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? "PMO,COMMERCIAL", Link = ConFacts.Link(item.ContractId ?? 0, "reviews", item.TargetId), Title = "پذیرش بازنگری قرارداد " + item.TargetNumber };
    }

    // ================================================================================================================================ station 3

    internal abstract class InitStep : ConStep
    {
        public override int StationNo => 3;
        public override string AgentCode => AgentCodes.Init;

        protected static async Task<List<AgentWorkItem>> ItemsAsync(IUnitOfWork uow, DateTime today, IQueryable<int> where, Func<ContractEntity, string> summary, string roles, CancellationToken ct)
        {
            List<ContractEntity> rows = await ConFacts.Live(uow, today).Where(c => where.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(c => ConFacts.ContractItem(c, summary(c), roles)).ToList();
        }

        protected static AgentGateInfo Info(AgentWorkItem item, string tab, string title, string roles = null) =>
            new AgentGateInfo { Roles = roles, Link = ConFacts.Link(item.TargetId, tab), Title = title + " " + item.TargetNumber };

        protected static async Task<AgentStepResult> ActAsync(IServiceProvider services, AgentWorkItem item, PcInitiationStep step, string note, string doneText, CancellationToken ct)
        {
            ResponseDto<PcInitiationDto> done = await Pc(services).InitiationActionAsync(new PcInitiationActionDto { ContractId = item.TargetId, Step = step, Note = note }, ct);
            if (!done.Success) return ProcStepCommon.Refusal(done);
            return AgentStepResult.Done("Contract", item.TargetId, item.TargetNumber, doneText);
        }
    }

    /// <summary>INIT.START: a contract with an accepted review gets its initiation record.</summary>
    internal sealed class InitStartStep : InitStep
    {
        public override string Code => "INIT.START";
        public override string Title => "ایجنت آغاز پروژه: ثبت فرآیند شروع پروژه";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> started = uow.Set<PcProjectInitiation>().AsNoTracking().Where(i => !i.Deleted).Select(i => i.ContractId);
            IQueryable<int> accepted = ConFacts.AcceptedContracts(uow);
            return ItemsAsync(uow, today, accepted.Where(id => !started.Contains(id)), c => "بازنگری قرارداد پذیرفته شده و فرآیند شروع پروژه هنوز ثبت نشده است.", null, ct);
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PcInitiationDto> done = await Pc(services).StartInitiationAsync(item.TargetId, ct);
            if (!done.Success) return ProcStepCommon.Refusal(done);
            return AgentStepResult.Done("Contract", item.TargetId, item.TargetNumber, $"ایجنت فرآیند شروع پروژه قرارداد {item.TargetNumber} را ثبت کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "initiation", "شروع پروژه");
    }

    /// <summary>INIT.CODE: the project code of the contract, the category from the contract record (equipment, spare parts or services).</summary>
    internal sealed class InitCodeStep : InitStep
    {
        public override string Code => "INIT.CODE";
        public override string Title => "ایجنت آغاز پروژه: صدور کد قرارداد";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public static async Task<List<ContractEntity>> CandidatesAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> pending = ConFacts.StepPending(uow, PcInitiationStep.CodeAssigned);
            IQueryable<int> coded = uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.ProjectCode != null).Select(t => t.ContractId);
            IQueryable<int> accepted = ConFacts.AcceptedContracts(uow);
            return await ConFacts.Live(uow, today).Where(c => pending.Contains(c.Id) && accepted.Contains(c.Id) && !coded.Contains(c.Id)).OrderBy(c => c.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
        }

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var items = new List<AgentWorkItem>();
            foreach (ContractEntity c in await CandidatesAsync(uow, today, ct))
                if (await ConFacts.CategoryAsync(uow, c, ct) != null) items.Add(ConFacts.ContractItem(c, "کد قرارداد هنوز صادر نشده است."));
            return items;
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            ContractEntity c = await ContractAsync(uow, item.TargetId, ct);
            PcCategory? category = c == null ? null : await ConFacts.CategoryAsync(uow, c, ct);
            if (category == null) return AgentStepResult.Waiting("گروه قرارداد از اطلاعات آن مشخص نیست.");
            ResponseDto<PcContractTermsDto> done = await Pc(services).GenerateProjectCodeAsync(new GenerateProjectCodeDto { ContractId = c.Id, Category = category.Value }, ct);
            if (!done.Success) return ProcStepCommon.Refusal(done);
            return AgentStepResult.Done("Contract", c.Id, c.Code, $"ایجنت کد {done.Data.ProjectCode} را برای قرارداد {c.Code} صادر کرد (گروه {category.Value}).");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "terms", "کد قرارداد");
    }

    /// <summary>INIT.CODE.INPUT (Input): the category of a contract that has no equipment, no services text and no type is not in the system.</summary>
    internal sealed class InitCodeInputStep : InitStep
    {
        public override string Code => "INIT.CODE.INPUT";
        public override string Title => "تعیین گروه قرارداد برای صدور کد";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var items = new List<AgentWorkItem>();
            foreach (ContractEntity c in await InitCodeStep.CandidatesAsync(uow, today, ct))
                if (await ConFacts.CategoryAsync(uow, c, ct) == null)
                    items.Add(ConFacts.ContractItem(c, "گروه قرارداد (ساخت، قطعات یدکی یا خدمات) از اطلاعات قرارداد معلوم نیست: نه تجهیزی ثبت شده نه شرح خدماتی. گروه را مشخص و کد را صادر کنید."));
            return items;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "terms", "تعیین گروه و صدور کد", "PLANNING");
    }

    /// <summary>INIT.UNITS: the notice of the contract to the units (the system draft FO-PL-001 is the note of the step).</summary>
    internal sealed class InitUnitsStep : InitStep
    {
        public override string Code => "INIT.UNITS";
        public override string Title => "ایجنت آغاز پروژه: اطلاع‌رسانی قرارداد به واحدها";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> done = ConFacts.StepDone(uow, PcInitiationStep.CodeAssigned);
            return ItemsAsync(uow, today, ConFacts.StepPending(uow, PcInitiationStep.UnitsNotified).Where(id => done.Contains(id)), c => "کد قرارداد صادر شده و واحدها هنوز مطلع نشده‌اند.", null, ct);
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<PcInitiationDto> now = await Pc(services).GetInitiationAsync(item.TargetId, ct);
            string note = now.Success ? now.Data.UnitsNoticeDraft : null;
            return await ActAsync(services, item, PcInitiationStep.UnitsNotified, note, $"ایجنت واحدها را از قرارداد {item.TargetNumber} مطلع کرد (فرم FO-PL-001).", ct);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "initiation", "اطلاع‌رسانی به واحدها");
    }

    /// <summary>INIT.MANAGER (gate): the CEO selects the project manager.</summary>
    internal sealed class InitManagerStep : InitStep
    {
        public override string Code => "INIT.MANAGER";
        public override string Title => "انتخاب مدیر پروژه توسط مدیرعامل";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> done = ConFacts.StepDone(uow, PcInitiationStep.UnitsNotified);
            List<AgentWorkItem> items = await ItemsAsync(uow, today, ConFacts.StepPending(uow, PcInitiationStep.ManagerSelected).Where(id => done.Contains(id)), c => null, "CEO", ct);
            if (items.Count == 0) return items;
            // the load of the managers (current contracts each already runs) helps the choice
            var loads = await uow.Set<PcContractTerms>().AsNoTracking().Where(t => !t.Deleted && t.ProjectManagerUserId != null && t.Contract.Status == ContractStatus.Current && !t.Contract.Deleted)
                .GroupBy(t => t.ProjectManagerUserId.Value).Select(g => new { Id = g.Key, Count = g.Count() }).OrderBy(x => x.Count).ThenBy(x => x.Id).Take(8).ToListAsync(ct);
            Dictionary<int, string> names = await InventoryUsers.GetNamesAsync(uow, loads.Select(l => (int?)l.Id), ct);
            string load = loads.Count == 0 ? "هنوز مدیر پروژه‌ای با قرارداد جاری ثبت نشده است." : "بار فعلی مدیران پروژه (قرارداد جاری): " + string.Join("، ", loads.Select(l => (names.TryGetValue(l.Id, out string n) ? n : "کاربر " + l.Id) + " " + l.Count));
            foreach (AgentWorkItem i in items) i.Summary = "مدیر پروژه را انتخاب کنید. " + load;
            return items;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "initiation", "انتخاب مدیر پروژه", "CEO");
    }

    /// <summary>INIT.CHARTER.DRAFT: the charter written from the contract, the terms and the equipment (no empty skeleton).</summary>
    internal sealed class CharterDraftStep : InitStep
    {
        public override string Code => "INIT.CHARTER.DRAFT";
        public override string Title => "ایجنت آغاز پروژه: پیش‌نویس منشور پروژه از اطلاعات قرارداد";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> drafted = uow.Set<PcProjectCharter>().AsNoTracking().Where(x => !x.Deleted && x.ScopeDescription != null && x.ScopeDescription != "").Select(x => x.ContractId);
            IQueryable<int> coded = ConFacts.StepDone(uow, PcInitiationStep.CodeAssigned);
            return ItemsAsync(uow, today, coded.Where(id => !drafted.Contains(id)), c => "منشور پروژه هنوز پیش‌نویس نشده است.", null, ct);
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            ContractEntity c = await ContractAsync(uow, item.TargetId, ct);
            PcContractTerms t = c == null ? null : await uow.Set<PcContractTerms>().AsNoTracking().FirstOrDefaultAsync(x => x.ContractId == c.Id && !x.Deleted, ct);
            if (c == null || t == null) return AgentStepResult.Waiting("قرارداد یا شرایط آن پیدا نشد.");
            List<(string Title, int Count)> eq = await ConFacts.EquipmentLinesAsync(uow, c.Id, ct);
            var workOrders = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.ContractId == c.Id && !w.Deleted).OrderBy(w => w.Id).Select(w => new { w.Code, w.Title, w.Count }).ToListAsync(ct);
            string items = eq.Count == 0 ? (string.IsNullOrWhiteSpace(c.Services) ? c.Title : c.Services) : string.Join("؛ ", eq.Select(e => e.Title + " × " + e.Count));
            DateTime? start = t.ScheduleStartDate ?? ConFacts.Real(c.StartDate);
            int? days = start.HasValue && t.ContractDeliveryDate.HasValue ? (int)(t.ContractDeliveryDate.Value - start.Value).TotalDays : (int?)null;
            decimal weekly = (t.ValueExVat ?? 0m) * (t.LatePenaltyPercentPerWeek ?? 0m) / 100m;
            var dto = new PcCharterDto
            {
                ContractId = c.Id,
                Overview = $"قرارداد {c.Code} «{c.Title}» با {c.Employer ?? "کارفرما"} با کد پروژه {t.ProjectCode}" + (string.IsNullOrWhiteSpace(c.Site) ? "." : "، محل: " + c.Site + "."),
                ScopeDescription = "تامین، ساخت و تحویل: " + items + ".",
                Requirements = $"الزامات مطابق متن قرارداد {c.EmployerContractNo ?? c.Code}، استانداردها و ITP محصول؛ بازرسی ورودی و گواهی مواد؛ ضمانت {t.WarrantyMonthsAfterCommissioning} ماه پس از راه‌اندازی یا {t.WarrantyMonthsAfterDelivery} ماه پس از تحویل (هرکدام زودتر).",
                Deliverables = items + (workOrders.Count > 0 ? ". دستورکارها: " + string.Join("، ", workOrders.Take(10).Select(w => w.Code + " " + w.Title + " × " + w.Count)) : ""),
                Boundaries = "آنچه در فهرست تجهیز و خدمات قرارداد نیامده بیرون از محدوده است؛ تغییر محدوده فقط از مسیر الحاقیه تاییدشده یا دستور تغییر.",
                TimeBaselineEstimate = $"شروع {ConFacts.Date(start)} تا تحویل قراردادی {ConFacts.Date(t.ContractDeliveryDate)}" + (days.HasValue ? $" ({days} روز)" : "") + ".",
                CostEstimate = $"مبلغ قرارداد {ConFacts.Money(t.ValueExVat)} {t.CurrencyCode} بدون مالیات؛ ذخیره مدیریتی {ConFacts.Money(t.ManagementReserve)}؛ جریمه تاخیر تا حدود {ConFacts.Money(weekly)} {t.CurrencyCode} در هفته؛ برآورد هزینه ساخت پس از BOM و بالانس مواد روشن می‌شود.",
                AlternativesAnalysis = "تصمیم ساخت یا خرید هر قلم پس از BOM در برنامه اجرایی (Make/Buy) و فرآیند تدارکات گرفته می‌شود.",
                Assumptions = $"پیش‌پرداخت {ConFacts.Pct(t.AdvancePercent)}، حسن انجام کار {ConFacts.Pct(t.RetentionPercent)}؛ مهلت بررسی مدارک توسط کارفرما {t.ClientReviewDays} روز.",
                Constraints = $"جریمه تاخیر {ConFacts.Pct(t.LatePenaltyPercentPerWeek)} در هفته تا سقف {ConFacts.Pct(t.PenaltyCapPercent)} پس از {t.GraceDays} روز مهلت؛ تاریخ تحویل قراردادی فقط با الحاقیه یا دستور تغییر جابه‌جا می‌شود.",
                Risks = $"جریمه تاخیر طبق بند جرایم قرارداد (مبلغ آن فقط در بخش برآورد هزینه)؛ تاخیر تامین اقلام بلندمدت؛ تغییر الزامات کارفرما.",
                Stakeholders = $"کارفرما: {c.Employer ?? "نامشخص"}" + (string.IsNullOrWhiteSpace(c.SubEmployer) ? "" : "؛ زیرمجموعه: " + c.SubEmployer) + "؛ واحدهای داخلی: مهندسی، برنامه‌ریزی، تولید، کنترل کیفیت، بازرگانی، انبار.",
                PreAssignedResources = "مدیر پروژه با انتخاب مدیرعامل؛ منابع واحدها طبق برنامه اجرایی.",
                ManagerAuthority = "مدیر پروژه در حدود این منشور و برنامه اجرایی تاییدشده درباره زمان‌بندی، منابع و ارتباط با کارفرما تصمیم می‌گیرد؛ تغییر مبلغ، تاریخ تحویل و محدوده با الحاقیه یا دستور تغییر است.",
            };
            ResponseDto<PcCharterDto> saved = await Pc(services).SaveCharterAsync(dto, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PcProjectCharter", c.Id, c.Code, $"ایجنت منشور پروژه {c.Code} را از اطلاعات قرارداد، شرایط و تجهیزات پیش‌نویس کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "charter", "منشور پروژه");
    }

    /// <summary>INIT.CHARTER.ACCEPT (gate): the project manager chosen by the CEO accepts the authority and the commitment.</summary>
    internal sealed class CharterAcceptStep : InitStep
    {
        public override string Code => "INIT.CHARTER.ACCEPT";
        public override string Title => "پذیرش اختیارات و تعهد منشور توسط مدیر پروژه";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> waiting = uow.Set<PcProjectCharter>().AsNoTracking().Where(x => !x.Deleted && x.ScopeDescription != null && x.ScopeDescription != "" && x.ManagerAcceptedAt == null && x.ApprovedAt == null).Select(x => x.ContractId);
            IQueryable<int> done = ConFacts.StepDone(uow, PcInitiationStep.ManagerSelected);
            return ItemsAsync(uow, today, done.Where(id => waiting.Contains(id)), c => "مدیر پروژه منصوب‌شده اختیارات و تعهد منشور را بپذیرد.", "PROJECTMANAGER", ct);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "charter", "پذیرش منشور", "PROJECTMANAGER");
    }

    /// <summary>INIT.CHARTER.APPROVE (gate): the CEO approves the charter.</summary>
    internal sealed class CharterApproveStep : InitStep
    {
        public override string Code => "INIT.CHARTER.APPROVE";
        public override string Title => "تایید منشور پروژه توسط مدیرعامل";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> waiting = uow.Set<PcProjectCharter>().AsNoTracking().Where(x => !x.Deleted && x.ScopeDescription != null && x.ScopeDescription != "" && x.ManagerAcceptedAt != null && x.ApprovedAt == null).Select(x => x.ContractId);
            return ItemsAsync(uow, today, waiting, c => "منشور پروژه پس از پذیرش مدیر پروژه منتظر تایید مدیرعامل است.", "CEO", ct);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "charter", "تایید منشور", "CEO");
    }

    /// <summary>INIT.PEP.DRAFT: the project execution plan written from the contract, the terms, the charter and the work orders.</summary>
    internal sealed class PepDraftStep : InitStep
    {
        public override string Code => "INIT.PEP.DRAFT";
        public override string Title => "ایجنت آغاز پروژه: پیش‌نویس برنامه اجرایی پروژه (PEP) از اطلاعات قرارداد";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> drafted = uow.Set<PcProjectPlan>().AsNoTracking().Where(x => !x.Deleted && x.ScopeStatement != null && x.ScopeStatement != "").Select(x => x.ContractId);
            IQueryable<int> done = ConFacts.StepDone(uow, PcInitiationStep.ManagerSelected);
            return ItemsAsync(uow, today, done.Where(id => !drafted.Contains(id)), c => "برنامه اجرایی پروژه هنوز پیش‌نویس نشده است.", null, ct);
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            ContractEntity c = await ContractAsync(uow, item.TargetId, ct);
            PcContractTerms t = c == null ? null : await uow.Set<PcContractTerms>().AsNoTracking().FirstOrDefaultAsync(x => x.ContractId == c.Id && !x.Deleted, ct);
            if (c == null || t == null) return AgentStepResult.Waiting("قرارداد یا شرایط آن پیدا نشد.");
            PcProjectCharter charter = await uow.Set<PcProjectCharter>().AsNoTracking().FirstOrDefaultAsync(x => x.ContractId == c.Id && !x.Deleted, ct);
            List<(string Title, int Count)> eq = await ConFacts.EquipmentLinesAsync(uow, c.Id, ct);
            var workOrders = await uow.Set<WorkOrder>().AsNoTracking().Where(w => w.ContractId == c.Id && !w.Deleted).OrderBy(w => w.Id).Select(w => new { w.Id, w.Code, w.Title, w.Count, w.StartDate }).ToListAsync(ct);
            Dictionary<int, DateTime?> planned = (await uow.Set<PcWorkOrderInfo>().AsNoTracking().Where(i => i.ContractId == c.Id && !i.Deleted).Select(i => new { i.WorkOrderId, i.PlannedDeliveryDate }).ToListAsync(ct))
                .GroupBy(i => i.WorkOrderId).ToDictionary(g => g.Key, g => g.First().PlannedDeliveryDate);
            string[] weekdays = { "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه" };
            string items = eq.Count == 0 ? (string.IsNullOrWhiteSpace(c.Services) ? c.Title : c.Services) : string.Join("؛ ", eq.Select(e => e.Title + " × " + e.Count));
            DateTime? start = t.ScheduleStartDate ?? ConFacts.Real(c.StartDate);
            var milestones = new List<string> { "شروع برنامه: " + ConFacts.Date(start), "تحویل قراردادی: " + ConFacts.Date(t.ContractDeliveryDate) };
            foreach (var w in workOrders.Take(12)) milestones.Add($"دستورکار {w.Code}: شروع {ConFacts.Date(w.StartDate?.Date)}، تحویل برنامه‌ای {ConFacts.Date(planned.TryGetValue(w.Id, out DateTime? p) ? p : null)}");
            var dto = new PcPlanDto
            {
                ContractId = c.Id,
                Overview = $"برنامه اجرایی قرارداد {c.Code} (کد پروژه {t.ProjectCode}) برای «{c.Title}» با {c.Employer ?? "کارفرما"}.",
                ScopeStatement = "محدوده کار: " + items + (charter?.ScopeDescription != null ? ". منشور: " + charter.ScopeDescription : "") + ".",
                InScope = items,
                OutOfScope = "هر آنچه در فهرست تجهیز و خدمات قرارداد و الحاقیه‌های تاییدشده نیامده است.",
                Constraints = $"جریمه تاخیر {ConFacts.Pct(t.LatePenaltyPercentPerWeek)} در هفته تا سقف {ConFacts.Pct(t.PenaltyCapPercent)}؛ تحویل قراردادی {ConFacts.Date(t.ContractDeliveryDate)}.",
                Assumptions = charter?.Assumptions ?? $"پیش‌پرداخت {ConFacts.Pct(t.AdvancePercent)} و حسن انجام کار {ConFacts.Pct(t.RetentionPercent)}.",
                Milestones = string.Join("\n", milestones),
                LongLeadRules = "اقلام با زمان تامین طولانی از بالانس مواد پروژه تعیین و درخواست خرید آن‌ها پیش از شروع ساخت صادر می‌شود؛ ایجنت برنامه‌ریزی کمبود را از BOM می‌سنجد.",
                CommunicationPlan = $"گزارش ماهانه کارفرما در روز {(t.ClientReportDayOfMonth.HasValue ? t.ClientReportDayOfMonth.Value.ToString(CultureInfo.InvariantCulture) : "تعیین‌نشده")} ماه؛ تاریخ داده هفتگی هر {weekdays[Math.Abs(t.DataDateWeekday) % 7]}؛ مهلت پاسخ کارفرما به مدارک {t.ClientReviewDays} روز.",
                MakeBuyPartner = "ساخت یا خرید هر قلم پس از BOM بر پایه ظرفیت و بالانس مواد؛ خرید از مسیر تدارکات (درخواست، استعلام، سفارش).",
                QualityPlan = "ITP مطابق نوع محصول، بازرسی ورودی توسط کنترل کیفیت، گواهی مواد، بازرسی نهایی و تحویل با پرونده کیفیت.",
                ChangeManagement = t.ChangeProcedureAgreed ? "روش تغییر با کارفرما توافق شده است؛ تغییرات فقط با الحاقیه یا دستور تغییر." : "تغییر مبلغ، تاریخ و محدوده فقط با الحاقیه تاییدشده مدیرعامل یا دستور تغییر تاییدشده.",
            };
            ResponseDto<PcPlanDto> saved = await Pc(services).SavePlanAsync(dto, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PcProjectPlan", c.Id, c.Code, $"ایجنت برنامه اجرایی {c.Code} را با {milestones.Count} نقطه عطف از قرارداد و دستورکارها پیش‌نویس کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "plan", "برنامه اجرایی");
    }

    /// <summary>INIT.PEP.APPROVE (gate): the project manager or the CEO approves the plan (never the one who drafted it, which the agent is).</summary>
    internal sealed class PepApproveStep : InitStep
    {
        public override string Code => "INIT.PEP.APPROVE";
        public override string Title => "تایید برنامه اجرایی پروژه (PEP)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> waiting = uow.Set<PcProjectPlan>().AsNoTracking().Where(x => !x.Deleted && x.ScopeStatement != null && x.ScopeStatement != "" && x.ApprovedAt == null).Select(x => x.ContractId);
            IQueryable<int> done = ConFacts.StepDone(uow, PcInitiationStep.CharterApproved);
            return ItemsAsync(uow, today, done.Where(id => waiting.Contains(id)), c => "برنامه اجرایی پیش‌نویس‌شده منتظر تایید مدیر پروژه یا مدیرعامل است.", "PROJECTMANAGER,CEO", ct);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "plan", "تایید برنامه اجرایی", "PROJECTMANAGER,CEO");
    }

    /// <summary>INIT.KOM (physical): the kick-off meeting with the client is held by the project manager.</summary>
    internal sealed class KickOffStep : InitStep
    {
        public override string Code => "INIT.KOM";
        public override string Title => "برگزاری جلسه شروع با کارفرما";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> done = ConFacts.StepDone(uow, PcInitiationStep.PepApproved);
            return ItemsAsync(uow, today, ConFacts.StepPending(uow, PcInitiationStep.KomHeld).Where(id => done.Contains(id)), c => "برنامه اجرایی تایید شده است؛ جلسه شروع با کارفرما برگزار و ثبت شود.", "PROJECTMANAGER", ct);
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "initiation", "جلسه شروع با کارفرما", "PROJECTMANAGER");
    }

    /// <summary>INIT.BOM: the request of the BOM from engineering is recorded by the planning agent.</summary>
    internal sealed class BomRequestStep : InitStep
    {
        public override string Code => "INIT.BOM";
        public override string Title => "ایجنت آغاز پروژه: ثبت درخواست BOM از مهندسی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => "PLANNING,PMO";

        public override Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<int> done = ConFacts.StepDone(uow, PcInitiationStep.KomHeld);
            return ItemsAsync(uow, today, ConFacts.StepPending(uow, PcInitiationStep.BomRequested).Where(id => done.Contains(id)), c => "جلسه شروع برگزار شده و BOM هنوز از مهندسی درخواست نشده است.", null, ct);
        }

        public override Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct) =>
            ActAsync(services, item, PcInitiationStep.BomRequested, "درخواست BOM قرارداد " + item.TargetNumber + " از مهندسی توسط ایجنت ثبت شد.", $"ایجنت درخواست BOM قرارداد {item.TargetNumber} را از مهندسی ثبت کرد.", ct);

        public override AgentGateInfo GateInfo(AgentWorkItem item) => Info(item, "initiation", "درخواست BOM");
    }
}
