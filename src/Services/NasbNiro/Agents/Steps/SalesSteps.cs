using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.ProjectControl;
using Entities.Model.NasbNiro.Tenders;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Sales;
using ViewModels.NasbNiro.SalesDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // Station 1 (sales: inquiry to won or lost proposal). The inquiry itself comes from the customer and is registered by a person: it is not a task. The bid checklist holds judgements (technical capability,
    // the sanction check, the risk reviews, the expected margin), so it is an Input; the agent sends a complete checklist to the CEO. The CEO decides to bid. The agent estimates a repeat product from the
    // equipment templates of the latest won inquiry of the same product type, finalises an estimate that has every cost, and drafts the proposal from the final estimate with the terms of the customer's latest
    // proposal. Sales or the CEO approves it (the CEO alone above the threshold or below cost), a person sends it to the customer and a person records the answer.

    public static class SalesSteps
    {
        public static IServiceCollection AddSalesSteps(this IServiceCollection services)
        {
            services.AddScoped<IAgentStep, SlBidDraftStep>();
            services.AddScoped<IAgentStep, SlBidSubmitStep>();
            services.AddScoped<IAgentStep, SlBidDecideStep>();
            services.AddScoped<IAgentStep, SlEstimateFromEquipmentStep>();
            services.AddScoped<IAgentStep, SlEstimateInputStep>();
            services.AddScoped<IAgentStep, SlEstimateFinalizeStep>();
            services.AddScoped<IAgentStep, SlProposalDraftStep>();
            services.AddScoped<IAgentStep, SlProposalApproveStep>();
            services.AddScoped<IAgentStep, SlTenderReviewStep>();
            services.AddScoped<IAgentStep, SlProposalSendStep>();
            services.AddScoped<IAgentStep, SlProposalResultStep>();
            return services;
        }
    }

    internal abstract class SlStep : ProcStep
    {
        public override int Station => 1;
        public override string AgentCode => AgentCodes.Sales;

        /// <summary>An inquiry older than this (days since it was received) is not started by the agents.</summary>
        public const int LiveDays = 365;

        protected const string InquiriesLink = "/sales/inquiries";

        protected static string InquiryLink(int id) => InquiriesLink + "/" + id.ToString(CultureInfo.InvariantCulture);
        protected static string BidLink(int inquiryId) => "/sales/bid-decisions/" + inquiryId.ToString(CultureInfo.InvariantCulture);
        protected static string EstimateLink(int id) => "/sales/estimates/" + id.ToString(CultureInfo.InvariantCulture);
        protected static string ProposalLink(int id) => "/sales/proposals/" + id.ToString(CultureInfo.InvariantCulture);

        protected static IQueryable<SlInquiry> Live(IUnitOfWork uow, DateTime today)
        {
            DateTime since = today.Date.AddDays(-LiveDays);
            return uow.Set<SlInquiry>().AsNoTracking().Where(i => !i.Deleted && i.ReceivedDate >= since);
        }

        protected static string Money(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>The equipment templates of the latest won inquiry of the same product type (a repeat product): the only way the agent can know which templates an inquiry is for.</summary>
    internal static class SlPrecedent
    {
        public sealed class Precedent
        {
            public int InquiryId { get; set; }
            public decimal? Overhead { get; set; }
            public decimal? Contingency { get; set; }
            public decimal? Margin { get; set; }
            public List<SlEstimateEquipmentInputDto> Equipments { get; } = new List<SlEstimateEquipmentInputDto>();
        }

        public sealed class Asked
        {
            public int Id { get; set; }
            public SlProductType Type { get; set; }
            public decimal Quantity { get; set; }
        }

        /// <summary>The precedent of each asked inquiry that has one whose counts scale to whole numbers.</summary>
        public static async Task<Dictionary<int, Precedent>> ForAsync(IUnitOfWork uow, List<Asked> asked, CancellationToken ct)
        {
            var result = new Dictionary<int, Precedent>();
            if (asked.Count == 0) return result;
            List<SlProductType> types = asked.Select(a => a.Type).Distinct().ToList();
            var rows = await (from ee in uow.Set<SlEstimateEquipment>().AsNoTracking()
                              join e in uow.Set<SlEstimate>().AsNoTracking() on ee.EstimateId equals e.Id
                              join p in uow.Set<SlInquiry>().AsNoTracking() on e.InquiryId equals p.Id
                              where !e.Deleted && !p.Deleted && p.Status == SlInquiryStatus.Won && e.Status == SlEstimateStatus.Final && types.Contains(p.ProductType)
                                    && uow.Set<Equipment>().Any(q => q.Id == ee.EquipmentId && !q.Deleted)
                              orderby p.Id descending
                              select new { p.Id, p.ProductType, p.Quantity, e.OverheadPercent, e.ContingencyPercent, e.TargetMarginPercent, ee.EquipmentId, ee.Count }).Take(2000).ToListAsync(ct);
            foreach (Asked a in asked)
            {
                int? latest = rows.Where(r => r.ProductType == a.Type).Select(r => (int?)r.Id).FirstOrDefault();
                if (!latest.HasValue) continue;
                var mine = rows.Where(r => r.Id == latest.Value).ToList();
                decimal previous = mine[0].Quantity;
                if (previous <= 0 || a.Quantity <= 0) continue;
                var p = new Precedent { InquiryId = latest.Value, Overhead = mine[0].OverheadPercent, Contingency = mine[0].ContingencyPercent, Margin = mine[0].TargetMarginPercent };
                bool whole = true;
                foreach (var r in mine)
                {
                    decimal count = r.Count * a.Quantity / previous;
                    if (count < 1 || count != decimal.Truncate(count) || count > 1000) { whole = false; break; }
                    p.Equipments.Add(new SlEstimateEquipmentInputDto { EquipmentId = r.EquipmentId, Count = (int)count });
                }
                if (whole) result[a.Id] = p;
            }
            return result;
        }
    }

    /// <summary>
    /// SL.BID.DRAFT (Input): the bid checklist of an inquiry. Input because the checklist holds judgements that exist only in people (the technical capability, the commercial risk, the payment terms and the
    /// guarantees reviewed, the expected margin) and the sanction check that is made outside the system; the capacity is read by the service itself from the required delivery date of the inquiry.
    /// </summary>
    internal sealed class SlBidDraftStep : SlStep
    {
        public override string Code => "SL.BID.DRAFT";
        public override string Title => "تهیه چک‌لیست تصمیم شرکت در استعلام";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => SalesRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var inquiries = await Live(uow, today).Where(i => (i.Status == SlInquiryStatus.Received || i.Status == SlInquiryStatus.UnderReview)
                    && !uow.Set<SlBidDecision>().Any(d => d.InquiryId == i.Id && !d.Deleted && d.Status != SlBidStatus.Draft))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.Number, i.Title }).ToListAsync(ct);
            int[] ids = inquiries.Select(i => i.Id).ToArray();
            List<SlBidDecision> drafts = ids.Length == 0 ? new List<SlBidDecision>() : await uow.Set<SlBidDecision>().AsNoTracking().Where(d => !d.Deleted && ids.Contains(d.InquiryId)).ToListAsync(ct);
            var result = new List<AgentWorkItem>();
            foreach (var i in inquiries)
            {
                SlBidDecision d = drafts.FirstOrDefault(x => x.InquiryId == i.Id);
                if (d == null) { result.Add(Item("SlInquiry", i.Id, i.Number, null, null, $"برای استعلام {i.Number} ({i.Title}) چک‌لیست تصمیم‌گیری هنوز ثبت نشده است؛ توان فنی، ریسک بازرگانی، شرایط پرداخت، ضمانت‌ها، استعلام تحریم و حاشیه سود مورد انتظار را بنویسید.", SalesRoles.Writers)); continue; }
                List<string> missing = SlPipelineService.MissingChecklist(d);
                if (missing.Count > 0)
                    result.Add(Item("SlInquiry", i.Id, i.Number, null, null, $"چک‌لیست استعلام {i.Number} کامل نیست؛ موارد باقی‌مانده: " + string.Join("، ", missing) + ".", SalesRoles.Writers));
            }
            return result;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SalesRoles.Writers, Link = BidLink(item.TargetId), Title = "چک‌لیست تصمیم استعلام " + item.TargetNumber };
    }

    /// <summary>SL.BID.SUBMIT (Agent): a complete checklist goes to the CEO (the sales line of the service; the agent decides nothing).</summary>
    internal sealed class SlBidSubmitStep : SlStep
    {
        public override string Code => "SL.BID.SUBMIT";
        public override string Title => "ایجنت فروش: ارسال چک‌لیست کامل به مدیرعامل";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => SalesRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await (from d in uow.Set<SlBidDecision>().AsNoTracking()
                              join i in Live(uow, today) on d.InquiryId equals i.Id
                              where !d.Deleted && d.Status == SlBidStatus.Draft && (i.Status == SlInquiryStatus.Received || i.Status == SlInquiryStatus.UnderReview)
                              orderby d.Id
                              select new { Decision = d, i.Number }).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Where(r => SlPipelineService.MissingChecklist(r.Decision).Count == 0)
                .Select(r => Item("SlInquiry", r.Decision.InquiryId, r.Number, null, null, $"چک‌لیست استعلام {r.Number} کامل است و برای تصمیم مدیرعامل ارسال نشده است.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<SlBidDecisionDto> r = await services.GetRequiredService<ISlPipelineService>().SubmitBidDecisionAsync(new SlBidActionDto { InquiryId = item.TargetId }, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("SlBidDecision", r.Data.Id, item.TargetNumber, $"ایجنت فروش چک‌لیست استعلام {item.TargetNumber} را برای تصمیم مدیرعامل فرستاد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SalesRoles.Writers, Link = BidLink(item.TargetId), Title = "ارسال چک‌لیست استعلام " + item.TargetNumber };
    }

    /// <summary>SL.BID.DECIDE (gate): the CEO decides to bid or not (never the one who prepared the checklist).</summary>
    internal sealed class SlBidDecideStep : SlStep
    {
        public override string Code => "SL.BID.DECIDE";
        public override string Title => "تصمیم مدیرعامل برای شرکت در استعلام";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await (from d in uow.Set<SlBidDecision>().AsNoTracking()
                              join i in uow.Set<SlInquiry>().AsNoTracking() on d.InquiryId equals i.Id
                              where !d.Deleted && !i.Deleted && d.Status == SlBidStatus.WaitingDecision
                              orderby d.Id
                              select new { i.Id, i.Number, i.Title, d.ExpectedMarginPercent }).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return rows.Select(r => Item("SlInquiry", r.Id, r.Number, null, null,
                $"چک‌لیست استعلام {r.Number} ({r.Title}) کامل است" + (r.ExpectedMarginPercent.HasValue ? $"؛ حاشیه سود مورد انتظار {r.ExpectedMarginPercent.Value.ToString("0.##", CultureInfo.InvariantCulture)} درصد" : "") + "؛ منتظر تصمیم مدیرعامل (شرکت یا عدم شرکت).", SalesRoles.Ceo)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SalesRoles.Ceo, Link = BidLink(item.TargetId), Title = "تصمیم شرکت در استعلام " + item.TargetNumber };
    }

    /// <summary>SL.ESTIMATE.FROM-EQUIPMENT (Agent): the estimate of an inquiry the CEO decided to bid, from the equipment templates of the latest won inquiry of the same product type.</summary>
    internal sealed class SlEstimateFromEquipmentStep : SlStep
    {
        public override string Code => "SL.ESTIMATE.FROM-EQUIPMENT";
        public override string Title => "ایجنت فروش: برآورد از قالب تجهیزات محصول تکراری";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => SalesRoles.Writers;

        /// <summary>The Bidding inquiries without an active estimate, with what is derivable for them.</summary>
        internal static async Task<List<(int Id, string Number, string Title, SlPrecedent.Precedent Precedent)>> WithoutEstimateAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await Live(uow, today).Where(i => i.Status == SlInquiryStatus.Bidding && !uow.Set<SlEstimate>().Any(e => e.InquiryId == i.Id && !e.Deleted && e.Status != SlEstimateStatus.Superseded))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.Number, i.Title, i.ProductType, i.Quantity }).ToListAsync(ct);
            Dictionary<int, SlPrecedent.Precedent> found = await SlPrecedent.ForAsync(uow, rows.Select(r => new SlPrecedent.Asked { Id = r.Id, Type = r.ProductType, Quantity = r.Quantity }).ToList(), ct);
            return rows.Select(r => (r.Id, r.Number, r.Title, found.TryGetValue(r.Id, out SlPrecedent.Precedent p) ? p : null)).ToList();
        }

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await WithoutEstimateAsync(uow, today, ct)).Where(r => r.Precedent != null)
                .Select(r => Item("SlInquiry", r.Id, r.Number, null, null, $"مدیرعامل شرکت در استعلام {r.Number} را تایید کرده؛ محصول تکراری است و قالب تجهیزات استعلام برنده قبلی (شمارهٔ استعلام #{r.Precedent.InquiryId}) را دارد.")).ToList();

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            var found = (await WithoutEstimateAsync(uow, DateTime.Today, ct)).FirstOrDefault(r => r.Id == item.TargetId);
            if (found.Precedent == null) return AgentStepResult.Waiting("برای این استعلام قالب تجهیزات قابل تشخیص (محصول تکراری برنده) نیست.");
            var dto = new SlEstimateFromEquipmentDto { InquiryId = item.TargetId, Equipments = found.Precedent.Equipments, OverheadPercent = found.Precedent.Overhead, ContingencyPercent = found.Precedent.Contingency, TargetMarginPercent = found.Precedent.Margin };
            ResponseDto<SlEstimateDto> r = await services.GetRequiredService<ISlEstimateService>().FromEquipmentAsync(dto, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("SlEstimate", r.Data.Id, r.Data.Number, $"ایجنت فروش برآورد {r.Data.Number} را از قالب {found.Precedent.Equipments.Count.ToString(CultureInfo.InvariantCulture)} تجهیز استعلام برنده قبلی ساخت" + (r.Data.MissingInputs == null ? "." : "؛ ورودی‌های ناقص: " + r.Data.MissingInputs + "."));
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SalesRoles.Writers, Link = InquiryLink(item.TargetId), Title = "برآورد استعلام " + item.TargetNumber };
    }

    /// <summary>
    /// SL.ESTIMATE.INPUT (Input): (a) an inquiry the CEO decided to bid that is not a repeat product: which equipment templates it is for (or the cost lines) is a technical choice that exists only in the sales
    /// engineer, and (b) a draft estimate whose costs are incomplete (a purchase price, a subcontract price, a work centre rate): the numbers exist only outside the system.
    /// </summary>
    internal sealed class SlEstimateInputStep : SlStep
    {
        public override string Code => "SL.ESTIMATE.INPUT";
        public override string Title => "تکمیل برآورد (انتخاب قالب تجهیز یا هزینه‌های ناقص)";
        public override AgentStepKind Kind => AgentStepKind.Input;
        public override string FallbackRoles => SalesRoles.Writers;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var result = new List<AgentWorkItem>();
            foreach (var r in (await SlEstimateFromEquipmentStep.WithoutEstimateAsync(uow, today, ct)).Where(r => r.Precedent == null))
                result.Add(Item("SlInquiry", r.Id, r.Number, null, null, $"مدیرعامل شرکت در استعلام {r.Number} ({r.Title}) را تایید کرده؛ محصول تکراری برنده ندارد: قالب تجهیزات (یا ردیف‌های هزینه) را برای برآورد انتخاب کنید.", SalesRoles.Writers));
            var drafts = await (from e in uow.Set<SlEstimate>().AsNoTracking()
                                join i in Live(uow, today) on e.InquiryId equals i.Id
                                where !e.Deleted && e.Status == SlEstimateStatus.Draft && (e.MissingInputs != null || !e.Lines.Any()) && (i.Status == SlInquiryStatus.Bidding || i.Status == SlInquiryStatus.Submitted)
                                orderby e.Id
                                select new { e.Id, e.Number, e.MissingInputs, Empty = !e.Lines.Any() }).Take(ProcStepCommon.Cap).ToListAsync(ct);
            foreach (var e in drafts)
                result.Add(Item("SlEstimate", e.Id, e.Number, null, null, e.MissingInputs != null ? $"برآورد {e.Number} کامل نیست: {e.MissingInputs}. مقادیر را وارد کنید تا ایجنت آن را نهایی کند." : $"برآورد {e.Number} هیچ ردیف هزینه‌ای ندارد؛ ردیف‌های هزینه را وارد کنید تا ایجنت آن را نهایی کند.", SalesRoles.Writers));
            return result;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) =>
            new AgentGateInfo { Roles = SalesRoles.Writers, Link = item.TargetType == "SlEstimate" ? EstimateLink(item.TargetId) : InquiryLink(item.TargetId), Title = "تکمیل برآورد " + item.TargetNumber };
    }

    /// <summary>
    /// SL.ESTIMATE.FINALIZE (gate): a draft estimate that has every input and a price is finalised by sales. A gate and not an agent step because the finalisation stamps a decision column (FinalizedByUserId)
    /// that the data layer never lets a seat write: the person who finalises confirms the numbers the proposal will carry.
    /// </summary>
    internal sealed class SlEstimateFinalizeStep : SlStep
    {
        public override string Code => "SL.ESTIMATE.FINALIZE";
        public override string Title => "نهایی‌کردن برآورد کامل توسط فروش";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await (from e in uow.Set<SlEstimate>().AsNoTracking()
                    join i in Live(uow, today) on e.InquiryId equals i.Id
                    where !e.Deleted && e.Status == SlEstimateStatus.Draft && e.MissingInputs == null && e.Price != null && e.Lines.Any() && i.Status == SlInquiryStatus.Bidding
                    orderby e.Id
                    select new { e.Id, e.Number, e.Price }).Take(ProcStepCommon.Cap).ToListAsync(ct))
                .Select(e => Item("SlEstimate", e.Id, e.Number, null, null, $"برآورد {e.Number} کامل است (قیمت {Money(e.Price.Value)} ریال)؛ پس از بررسی اعداد، آن را نهایی کنید تا ایجنت پیشنهاد را پیش‌نویس کند.", SalesRoles.Writers)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SalesRoles.Writers, Link = EstimateLink(item.TargetId), Title = "نهایی‌کردن برآورد " + item.TargetNumber };
    }

    /// <summary>SL.PROPOSAL.DRAFT (Agent): the proposal of an inquiry with a final estimate and no proposal yet: the price of the estimate, the delivery weeks from the required delivery date, the terms of the customer's latest proposal.</summary>
    internal sealed class SlProposalDraftStep : SlStep
    {
        public override string Code => "SL.PROPOSAL.DRAFT";
        public override string Title => "ایجنت فروش: پیش‌نویس پیشنهاد از برآورد نهایی";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => SalesRoles.Writers;

        private static IQueryable<SlInquiry> Candidates(IUnitOfWork uow, DateTime today) =>
            Live(uow, today).Where(i => i.Status == SlInquiryStatus.Bidding && !uow.Set<SlProposal>().Any(p => p.InquiryId == i.Id && !p.Deleted)
                && uow.Set<SlEstimate>().Any(e => e.InquiryId == i.Id && !e.Deleted && e.Status == SlEstimateStatus.Final && e.Price != null));

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await Candidates(uow, today).OrderBy(i => i.Id).Take(ProcStepCommon.Cap).Select(i => new { i.Id, i.Number, i.Title }).ToListAsync(ct))
                .Select(i => Item("SlInquiry", i.Id, i.Number, null, null, $"برآورد استعلام {i.Number} ({i.Title}) نهایی است و هنوز پیشنهادی ساخته نشده است.")).ToList();

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            SlInquiry inquiry = await Candidates(uow, DateTime.Today).FirstOrDefaultAsync(i => i.Id == item.TargetId, ct);
            if (inquiry == null) return AgentStepResult.Waiting("استعلام برآورد نهایی بدون پیشنهاد ندارد.");
            int estimate = await uow.Set<SlEstimate>().AsNoTracking().Where(e => e.InquiryId == inquiry.Id && !e.Deleted && e.Status == SlEstimateStatus.Final && e.Price != null).OrderByDescending(e => e.Revision).Select(e => e.Id).FirstAsync(ct);
            SlProposal terms = await (from p in uow.Set<SlProposal>().AsNoTracking()
                                      join q in uow.Set<SlInquiry>().AsNoTracking() on p.InquiryId equals q.Id
                                      where !p.Deleted && q.CustomerId == inquiry.CustomerId && (p.Status == SlProposalStatus.Won || p.Status == SlProposalStatus.Lost || p.Status == SlProposalStatus.Submitted)
                                      orderby p.Id descending
                                      select p).FirstOrDefaultAsync(ct);
            var dto = new SlSaveProposalDto { InquiryId = inquiry.Id, EstimateId = estimate, CustomerReference = inquiry.CustomerReference };
            if (inquiry.RequiredDeliveryDate.HasValue && inquiry.RequiredDeliveryDate.Value.Date > DateTime.Today)
                dto.DeliveryWeeks = (int)Math.Ceiling((inquiry.RequiredDeliveryDate.Value.Date - DateTime.Today).TotalDays / 7d);
            if (terms != null)
            {
                dto.ValidityDays = terms.ValidityDays; dto.PaymentTermsText = terms.PaymentTermsText; dto.GuaranteesText = terms.GuaranteesText; dto.DeviationsText = terms.DeviationsText;
                dto.VatPercent = terms.VatPercent; dto.AdvancePercent = terms.AdvancePercent; dto.RetentionPercent = terms.RetentionPercent; dto.LatePenaltyPercentPerWeek = terms.LatePenaltyPercentPerWeek;
                dto.PenaltyCapPercent = terms.PenaltyCapPercent; dto.ClientReviewDays = terms.ClientReviewDays;
            }
            ResponseDto<SlProposalDto> r = await services.GetRequiredService<ISlProposalService>().SaveProposalAsync(dto, ct);
            if (!r.Success) return ProcStepCommon.Refusal(r);
            return AgentStepResult.Done("SlProposal", r.Data.Id, r.Data.Number, $"ایجنت فروش پیشنهاد {r.Data.Number} را به مبلغ {Money(r.Data.Price)} ریال پیش‌نویس کرد" + (terms == null ? " (مشتری پیشنهاد قبلی ندارد؛ شرایط پرداخت و ضمانت را فروش کامل کند)." : " با شرایط آخرین پیشنهاد همان مشتری.") + (r.Data.BelowCost ? " قیمت کمتر از بهای تمام‌شده است." : ""));
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SalesRoles.Writers, Link = InquiryLink(item.TargetId), Title = "پیش‌نویس پیشنهاد استعلام " + item.TargetNumber };
    }

    /// <summary>SL.PROPOSAL.APPROVE (gate): the CEO alone when the price is below cost, above the approval threshold, or the CEO has set no threshold; otherwise sales or the CEO, never a person who touched the proposal or the estimate.</summary>
    internal sealed class SlProposalApproveStep : SlStep
    {
        public override string Code => "SL.PROPOSAL.APPROVE";
        public override string Title => "تایید پیشنهاد (فروش یا مدیرعامل بر پایه سقف)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            decimal? threshold = await uow.Set<SlSetting>().AsNoTracking().Where(s => s.Key == SlSettingKeys.ProposalApprovalThreshold && !s.Deleted).Select(s => s.Value).FirstOrDefaultAsync(ct);
            var rows = await uow.Set<SlProposal>().AsNoTracking().Where(p => !p.Deleted && p.Status == SlProposalStatus.Draft).OrderBy(p => p.Id).Take(ProcStepCommon.Cap)
                .Select(p => new { p.Id, p.Number, p.Revision, p.Price, p.BelowCost }).ToListAsync(ct);
            return rows.Select(p =>
            {
                bool ceo = SlApproval.NeedsCeo(p.Price, p.BelowCost, threshold);
                string why = p.BelowCost ? "قیمت کمتر از بهای تمام‌شده است" : !threshold.HasValue ? "سقف تایید فروش را مدیرعامل هنوز تعیین نکرده" : p.Price > threshold.Value ? "قیمت از سقف تایید فروش بیشتر است" : null;
                return Item("SlProposal", p.Id, p.Number, null, null, $"پیشنهاد {p.Number} بازنگری {p.Revision} به مبلغ {Money(p.Price)} ریال منتظر تایید است" + (ceo ? "؛ " + why + "؛ فقط مدیرعامل تایید می‌کند." : "؛ فروش (غیر از تهیه‌کنندگان) یا مدیرعامل تایید می‌کند."), ceo ? SalesRoles.Ceo : SalesRoles.ProposalApprovers);
            }).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = item.GateRoles ?? SalesRoles.Ceo, Link = ProposalLink(item.TargetId), Title = "تایید پیشنهاد " + item.TargetNumber };
    }

    /// <summary>
    /// SL.TENDER.REVIEW (gate): the tender of the inquiry must be reviewed (ISO 9001 8.2.3) before the offer leaves the company: the commercial unit writes the checklist, then the PMO deputy and the commercial
    /// unit (two different people, neither of whom wrote it) accept it. The service refuses the sending of the proposal until then.
    /// </summary>
    internal sealed class SlTenderReviewStep : SlStep
    {
        public override string Code => "SL.TENDER.REVIEW";
        public override string Title => "بازنگری مناقصه پیش از ارسال پیشنهاد (PMO و بازرگانی)";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public const string Roles = "PMO,COMMERCIAL";

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await (from p in uow.Set<SlProposal>().AsNoTracking()
                              join i in uow.Set<SlInquiry>().AsNoTracking() on p.InquiryId equals i.Id
                              join t in uow.Set<Tender>().AsNoTracking() on i.TenderId equals t.Id
                              where !p.Deleted && !t.Deleted && p.Status == SlProposalStatus.Approved
                                    && !uow.Set<PcContractReview>().Any(r => !r.Deleted && r.Stage == PcReviewStage.Tender && r.TenderCode == t.Code && r.Decision == PcReviewDecision.Accept)
                              orderby p.Id
                              select new { p.Id, p.Number, TenderCode = t.Code }).Take(ProcStepCommon.Cap).ToListAsync(ct);
            string[] codes = rows.Select(r => r.TenderCode).Distinct().ToArray();
            List<string> started = codes.Length == 0 ? new List<string>() : await uow.Set<PcContractReview>().AsNoTracking()
                .Where(r => !r.Deleted && r.Stage == PcReviewStage.Tender && codes.Contains(r.TenderCode)).Select(r => r.TenderCode).Distinct().ToListAsync(ct);
            return rows.Select(r => Item("SlProposal", r.Id, r.Number, null, null,
                started.Contains(r.TenderCode)
                    ? $"بازنگری مناقصه {r.TenderCode} نوشته شده؛ منتظر پذیرش PMO و بازرگانی (دو نفر متفاوت) است؛ تا آن زمان پیشنهاد {r.Number} ارسال نمی‌شود."
                    : $"پیشنهاد {r.Number} تایید شده ولی بازنگری مناقصه {r.TenderCode} ثبت نشده است؛ بازرگانی چک‌لیست را بنویسد و PMO و بازرگانی (دو نفر متفاوت) آن را بپذیرند.", Roles)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = Roles, Link = ProposalLink(item.TargetId), Title = "بازنگری مناقصه پیشنهاد " + item.TargetNumber };
    }

    /// <summary>SL.PROPOSAL.SEND (Physical): the approved proposal is sent to the customer; the person records the sending on the proposal.</summary>
    internal sealed class SlProposalSendStep : SlStep
    {
        public override string Code => "SL.PROPOSAL.SEND";
        public override string Title => "ارسال پیشنهاد به مشتری";
        public override AgentStepKind Kind => AgentStepKind.Physical;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<SlProposal>().AsNoTracking().Where(p => !p.Deleted && p.Status == SlProposalStatus.Approved
                    && !uow.Set<SlInquiry>().Any(i => i.Id == p.InquiryId && i.TenderId != null
                        && uow.Set<Tender>().Any(t => t.Id == i.TenderId && !t.Deleted && !uow.Set<PcContractReview>().Any(r => !r.Deleted && r.Stage == PcReviewStage.Tender && r.TenderCode == t.Code && r.Decision == PcReviewDecision.Accept))))
                .OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Number }).ToListAsync(ct))
                .Select(p => Item("SlProposal", p.Id, p.Number, null, null, $"پیشنهاد {p.Number} تایید شده است؛ پس از ارسال برای مشتری، ارسال را روی پیشنهاد ثبت کنید.", SalesRoles.Writers)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SalesRoles.Writers, Link = ProposalLink(item.TargetId), Title = "ارسال پیشنهاد " + item.TargetNumber + " به مشتری" };
    }

    /// <summary>SL.PROPOSAL.RESULT (Input): won or lost. Input because the answer of the customer (and the competitor, the price and the reason) exists only outside the system.</summary>
    internal sealed class SlProposalResultStep : SlStep
    {
        public override string Code => "SL.PROPOSAL.RESULT";
        public override string Title => "ثبت نتیجه پیشنهاد (برنده یا بازنده)";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct) =>
            (await uow.Set<SlProposal>().AsNoTracking().Where(p => !p.Deleted && p.Status == SlProposalStatus.Submitted).OrderBy(p => p.Id).Take(ProcStepCommon.Cap).Select(p => new { p.Id, p.Number, p.SubmittedAt }).ToListAsync(ct))
                .Select(p => Item("SlProposal", p.Id, p.Number, null, null, $"پیشنهاد {p.Number} برای مشتری ارسال شده؛ پس از اعلام نتیجه از طرف مشتری، برنده یا بازنده بودن، دلیل و (در صورت بازنده) رقیب و قیمت او را ثبت کنید.", SalesRoles.ProposalApprovers)).ToList();

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = SalesRoles.ProposalApprovers, Link = ProposalLink(item.TargetId), Title = "ثبت نتیجه پیشنهاد " + item.TargetNumber };
    }
}
