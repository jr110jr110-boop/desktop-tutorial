using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Agents;
using Entities.Model.NasbNiro.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services.NasbNiro.Procurement;
using ViewModels.NasbNiro.ProcurementDtos;

namespace Services.NasbNiro.Agents.Steps
{
    // The inquiry: opened empty by the procurement agent (the buyer enters the quotes), judged technically by engineering or quality, budgeted by planning, the winner selected by the agent when
    // the rules allow it (the buyer when they do not), then the committee. Every gate lists the roles the service asks for.

    internal static class InquiryText
    {
        public static string Link(AgentWorkItem item) => "/procurement/inquiries/" + item.TargetId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>PROC.INQ.OPEN: an approved request that has no inquiry and no live order line gets an empty inquiry from the procurement agent.</summary>
    internal sealed class OpenInquiryStep : ProcStep
    {
        public override string Code => "PROC.INQ.OPEN";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "ایجنت تدارکات: باز کردن استعلام برای درخواست خرید تاییدشده";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ProcurementRoles.Commercial;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            IQueryable<PurchaseInquiry> inquiries = uow.Set<PurchaseInquiry>().AsNoTracking();
            IQueryable<PurchaseOrderLine> orderLines = uow.Set<PurchaseOrderLine>().AsNoTracking();
            // the vehicle forms and the progress statement are not bought by inquiry
            var rows = await uow.Set<PurchaseRequest>().AsNoTracking()
                .Where(r => !r.Deleted && r.Status == PurchaseRequestStatus.Approved
                            && r.FormType != PurchaseFormType.VehicleProject && r.FormType != PurchaseFormType.VehicleGeneral && r.FormType != PurchaseFormType.ProgressStatement
                            && !inquiries.Any(i => i.PurchaseRequestId == r.Id && !i.Deleted && i.Status != InquiryStatus.Cancelled)
                            && !orderLines.Any(l => l.RequestLine.PurchaseRequestId == r.Id && l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled && !l.PurchaseOrder.Deleted))
                .OrderBy(r => r.Id).Take(ProcStepCommon.Cap).Select(r => new { r.Id, r.Number, r.ContractId, r.WorkOrderId, r.EstimatedAmount }).ToListAsync(ct);
            return rows.Select(r => Item("PurchaseRequest", r.Id, r.Number, r.ContractId, r.WorkOrderId, $"درخواست خرید تاییدشده به مبلغ برآوردی {ProcStepCommon.Num(r.EstimatedAmount)} ریال استعلام ندارد.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            ResponseDto<InquiryDto> saved = await services.GetRequiredService<IProcurementService>().SaveInquiryAsync(new SaveInquiryDto
            {
                PurchaseRequestId = item.TargetId, InquiryDate = DateTime.Today, Description = "استعلام بدون پیشنهاد ساخته‌شده توسط ایجنت تدارکات؛ پیشنهاد تامین‌کنندگان را بازرگانی ثبت می‌کند.",
            }, ct);
            if (!saved.Success) return ProcStepCommon.Refusal(saved);
            return AgentStepResult.Done("PurchaseInquiry", saved.Data.Id, saved.Data.Number, $"ایجنت تدارکات استعلام {saved.Data.Number} را برای درخواست {item.TargetNumber} باز کرد.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = "/procurement/requests/" + item.TargetId.ToString(CultureInfo.InvariantCulture), Title = "باز کردن استعلام برای درخواست خرید " + item.TargetNumber };
    }

    /// <summary>PROC.INQ.QUOTES: the buyer enters the supplier quotes (or writes why fewer are taken) until the inquiry has as many usable quotes as its value needs.</summary>
    internal sealed class QuotesInputStep : ProcStep
    {
        public override string Code => "PROC.INQ.QUOTES";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "ثبت پیشنهاد تامین‌کنندگان در استعلام";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            // usable = not judged yet, or accepted technically; at least one quote is always needed
            var rows = await uow.Set<PurchaseInquiry>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == InquiryStatus.Open && (i.FewerQuotesReason == null || i.FewerQuotesReason == "")
                            && i.Quotes.Count(q => q.TechnicallyAccepted || q.TechnicalReviewedAt == null) < (i.RequiredQuotes < 1 ? 1 : i.RequiredQuotes))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap)
                .Select(i => new { i.Id, i.Number, ContractId = i.PurchaseRequest.ContractId, i.PurchaseRequest.WorkOrderId, Required = i.RequiredQuotes < 1 ? 1 : i.RequiredQuotes, Usable = i.Quotes.Count(q => q.TechnicallyAccepted || q.TechnicalReviewedAt == null) })
                .ToListAsync(ct);
            return rows.Select(r => Item("PurchaseInquiry", r.Id, r.Number, r.ContractId, r.WorkOrderId,
                $"{r.Usable} پیشنهاد قابل استفاده از {r.Required} پیشنهاد لازم ثبت شده است؛ پیشنهاد تامین‌کنندگان را وارد کنید یا علت پیشنهاد کمتر را بنویسید.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.Commercial, Link = InquiryText.Link(item), Title = "ثبت پیشنهادهای استعلام " + item.TargetNumber };
    }

    /// <summary>PROC.INQ.TECH: engineering or quality judges each quote (never the person who registered the inquiry).</summary>
    internal sealed class TechnicalGateStep : ProcStep
    {
        public override string Code => "PROC.INQ.TECH";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "ارزیابی فنی پیشنهادهای استعلام";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var rows = await uow.Set<PurchaseInquiry>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == InquiryStatus.Open && i.Quotes.Any(q => q.TechnicalReviewedAt == null))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap)
                .Select(i => new { i.Id, i.Number, ContractId = i.PurchaseRequest.ContractId, i.PurchaseRequest.WorkOrderId, Waiting = i.Quotes.Count(q => q.TechnicalReviewedAt == null) }).ToListAsync(ct);
            return rows.Select(r => Item("PurchaseInquiry", r.Id, r.Number, r.ContractId, r.WorkOrderId, $"{r.Waiting} پیشنهاد منتظر ارزیابی فنی است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.TechnicalAcceptance, Link = InquiryText.Link(item), Title = "ارزیابی فنی پیشنهادهای استعلام " + item.TargetNumber };
    }

    /// <summary>PROC.INQ.PLAN: planning approves the budget and the delivery time before the winner is selected (when the company setting asks for it).</summary>
    internal sealed class PlanningGateStep : ProcStep
    {
        public override string Code => "PROC.INQ.PLAN";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "تایید بودجه و زمان تحویل استعلام توسط برنامه‌ریزی";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            Dictionary<string, decimal> s = await ProcStepCommon.SettingsAsync(uow, ct);
            if (s[ProcurementSettingKeys.InquiryPlanningRequired] <= 0) return new List<AgentWorkItem>();
            var rows = await uow.Set<PurchaseInquiry>().AsNoTracking()
                .Where(i => !i.Deleted && i.Status == InquiryStatus.Open && i.PlanningApprovedAt == null && i.Quotes.Any(q => q.TechnicallyAccepted))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap)
                .Select(i => new { i.Id, i.Number, ContractId = i.PurchaseRequest.ContractId, i.PurchaseRequest.WorkOrderId, Accepted = i.Quotes.Count(q => q.TechnicallyAccepted) }).ToListAsync(ct);
            return rows.Select(r => Item("PurchaseInquiry", r.Id, r.Number, r.ContractId, r.WorkOrderId, $"{r.Accepted} پیشنهاد تایید فنی دارد؛ بودجه و زمان تحویل منتظر تایید برنامه‌ریزی است.")).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.Planning, Link = InquiryText.Link(item), Title = "تایید بودجه و زمان تحویل استعلام " + item.TargetNumber, HasOfficeInboxItem = true };
    }

    internal enum SelectionKind
    {
        NotReady = 0,
        Auto = 1,
        Manual = 2,
    }

    internal sealed class SelectionDecision
    {
        public SelectionKind Kind { get; set; }
        public SupplierQuote Quote { get; set; }
        public string ProviderName { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>The selection rules of SelectQuoteAsync, read as a question: may the agent select the lowest accepted quote, must the buyer, or is it too early.</summary>
    internal static class SelectionRules
    {
        public static SelectionDecision Evaluate(PurchaseInquiry inquiry, Dictionary<string, decimal> s, Dictionary<int, SupplierProfile> profiles, Dictionary<int, string> providerNames)
        {
            var none = new SelectionDecision { Kind = SelectionKind.NotReady };
            if (inquiry.Status != InquiryStatus.Open) return none;
            if (s[ProcurementSettingKeys.InquiryPlanningRequired] > 0 && !inquiry.PlanningApprovedAt.HasValue) return none;
            if (inquiry.Quotes.Any(q => q.TechnicalReviewedAt == null)) return none;
            List<SupplierQuote> accepted = inquiry.Quotes.Where(q => q.TechnicallyAccepted).OrderBy(q => q.TotalAmount).ThenBy(q => q.Id).ToList();
            if (accepted.Count == 0) return none;
            SupplierQuote lowest = accepted[0];
            string name = providerNames.TryGetValue(lowest.ProviderId, out string n) ? n : "#" + lowest.ProviderId;
            SelectionDecision Manual(string reason) => new SelectionDecision { Kind = SelectionKind.Manual, Quote = lowest, ProviderName = name, Reason = reason };

            int required = ProcurementAuthority.RequiredQuotes(s, inquiry.RequiredQuotes, lowest.TotalAmount);
            if (accepted.Count < required)
                return string.IsNullOrWhiteSpace(inquiry.FewerQuotesReason) ? none
                    : Manual($"فقط {accepted.Count} پیشنهاد قابل قبول فنی از {required} پیشنهاد لازم هست و بازرگانی علت آن را نوشته است؛ انتخاب با بازرگانی است (تایید مدیرعامل لازم می‌شود).");

            profiles.TryGetValue(lowest.ProviderId, out SupplierProfile profile);
            if (profile?.Blocked == true || profile?.Grade == SupplierGrade.D)
                return Manual($"کمترین پیشنهاد قابل قبول از {name} است و این تامین‌کننده در فهرست غیرمجاز (گرید D یا مسدود) است؛ ایجنت از او انتخاب نمی‌کند. پیشنهاد دیگر را با ذکر دلیل انتخاب کنید.");
            if ((profile == null || profile.Grade == SupplierGrade.NotEvaluated) && lowest.TotalAmount > s[ProcurementSettingKeys.PettyCashLimit])
                return Manual($"کمترین پیشنهاد قابل قبول از {name} است و این تامین‌کننده ارزیابی نشده است (ارزیابی اولیه لازم است)؛ انتخاب با بازرگانی است.");
            if (profile?.Grade == SupplierGrade.C && accepted.Any(q => profiles.TryGetValue(q.ProviderId, out SupplierProfile o) && (o.Grade == SupplierGrade.A || o.Grade == SupplierGrade.B) && !o.Blocked))
                return Manual($"کمترین پیشنهاد قابل قبول از {name} (گرید C) است در حالی که تامین‌کننده گرید A یا B هم پیشنهاد داده است؛ انتخاب با بازرگانی و با ذکر دلیل است.");
            if (inquiry.PlanningBudgetAmount.HasValue && lowest.TotalAmount > inquiry.PlanningBudgetAmount.Value)
                return Manual($"مبلغ کمترین پیشنهاد قابل قبول ({ProcStepCommon.Num(lowest.TotalAmount)} ریال) از بودجه تاییدشده برنامه‌ریزی ({ProcStepCommon.Num(inquiry.PlanningBudgetAmount.Value)} ریال) بیشتر است؛ استعلام را برای تایید دوباره برگردانید.");
            if (inquiry.PlanningDeliveryDays.HasValue && lowest.DeliveryDays.HasValue && lowest.DeliveryDays.Value > inquiry.PlanningDeliveryDays.Value)
                return Manual($"زمان تحویل کمترین پیشنهاد قابل قبول ({lowest.DeliveryDays} روز) از زمان تاییدشده برنامه‌ریزی ({inquiry.PlanningDeliveryDays} روز) بیشتر است؛ انتخاب با بازرگانی و با ذکر دلیل است.");
            return new SelectionDecision { Kind = SelectionKind.Auto, Quote = lowest, ProviderName = name };
        }

        /// <summary>The open inquiries that may be ready for a selection, with the decision for each (bounded, oldest first).</summary>
        public static async Task<List<(PurchaseInquiry Inquiry, SelectionDecision Decision)>> ScanAsync(IUnitOfWork uow, CancellationToken ct)
        {
            Dictionary<string, decimal> s = await ProcStepCommon.SettingsAsync(uow, ct);
            bool planning = s[ProcurementSettingKeys.InquiryPlanningRequired] > 0;
            List<PurchaseInquiry> list = await uow.Set<PurchaseInquiry>().AsNoTracking().Include(i => i.Quotes).Include(i => i.PurchaseRequest)
                .Where(i => !i.Deleted && i.Status == InquiryStatus.Open && (!planning || i.PlanningApprovedAt != null) && i.Quotes.Any(q => q.TechnicallyAccepted) && !i.Quotes.Any(q => q.TechnicalReviewedAt == null))
                .OrderBy(i => i.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            return await EvaluateAsync(uow, list, s, ct);
        }

        public static async Task<List<(PurchaseInquiry Inquiry, SelectionDecision Decision)>> EvaluateAsync(IUnitOfWork uow, List<PurchaseInquiry> list, Dictionary<string, decimal> s, CancellationToken ct)
        {
            List<int> providerIds = list.SelectMany(i => i.Quotes.Select(q => q.ProviderId)).Distinct().ToList();
            Dictionary<int, SupplierProfile> profiles = await uow.Set<SupplierProfile>().AsNoTracking().Where(p => providerIds.Contains(p.ProviderId)).ToDictionaryAsync(p => p.ProviderId, ct);
            Dictionary<int, string> names = await uow.Set<Entities.Model.NasbNiro.Providers.Provider>().AsNoTracking().Where(p => providerIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            return list.Select(i => (i, Evaluate(i, s, profiles, names))).ToList();
        }
    }

    /// <summary>PROC.INQ.SELECT: the procurement agent selects the lowest technically accepted quote when no rule asks for a reason; otherwise the buyer is asked (PROC.INQ.SELECT-MANUAL).</summary>
    internal sealed class SelectQuoteStep : ProcStep
    {
        public override string Code => "PROC.INQ.SELECT";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "ایجنت تدارکات: انتخاب کمترین پیشنهاد قابل قبول";
        public override AgentStepKind Kind => AgentStepKind.Agent;
        public override string FallbackRoles => ProcurementRoles.Commercial;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var scan = await SelectionRules.ScanAsync(uow, ct);
            return scan.Where(x => x.Decision.Kind == SelectionKind.Auto)
                .Select(x => Item("PurchaseInquiry", x.Inquiry.Id, x.Inquiry.Number, x.Inquiry.PurchaseRequest.ContractId, x.Inquiry.PurchaseRequest.WorkOrderId,
                    $"کمترین پیشنهاد قابل قبول: {x.Decision.ProviderName} با {ProcStepCommon.Num(x.Decision.Quote.TotalAmount)} ریال.")).ToList();
        }

        public override async Task<AgentStepResult> RunAsync(AgentWorkItem item, IServiceProvider services, CancellationToken ct)
        {
            IUnitOfWork uow = services.GetRequiredService<IUnitOfWork>();
            Dictionary<string, decimal> s = await ProcStepCommon.SettingsAsync(uow, ct);
            PurchaseInquiry inquiry = await uow.Set<PurchaseInquiry>().AsNoTracking().Include(i => i.Quotes).FirstOrDefaultAsync(i => i.Id == item.TargetId && !i.Deleted, ct);
            if (inquiry == null) return AgentStepResult.Waiting("استعلام پیدا نشد.");
            var decision = (await SelectionRules.EvaluateAsync(uow, new List<PurchaseInquiry> { inquiry }, s, ct))[0].Decision;
            if (decision.Kind == SelectionKind.NotReady) return AgentStepResult.Waiting("هنوز شرایط انتخاب برنده فراهم نیست.");
            if (decision.Kind == SelectionKind.Manual) return AgentStepResult.Waiting(decision.Reason);
            ResponseDto<InquiryDto> selected = await services.GetRequiredService<IProcurementService>().SelectQuoteAsync(new SelectQuoteDto { InquiryId = inquiry.Id, QuoteId = decision.Quote.Id }, ct);
            if (!selected.Success) return ProcStepCommon.Refusal(selected);
            return AgentStepResult.Done("PurchaseInquiry", inquiry.Id, inquiry.Number, $"ایجنت تدارکات پیشنهاد {decision.ProviderName} ({ProcStepCommon.Num(decision.Quote.TotalAmount)} ریال) را به‌عنوان کمترین پیشنهاد قابل قبول انتخاب کرد؛ تایید با کمیته است.");
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Link = InquiryText.Link(item), Title = "انتخاب برنده استعلام " + item.TargetNumber };
    }

    /// <summary>PROC.INQ.SELECT-MANUAL: the lowest quote cannot be selected by the agent (the supplier is not allowed, the time or budget differ from what planning approved, fewer quotes than needed): the buyer selects with a reason. The summary says why.</summary>
    internal sealed class SelectManualStep : ProcStep
    {
        public override string Code => "PROC.INQ.SELECT-MANUAL";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "انتخاب دستی برنده استعلام";
        public override AgentStepKind Kind => AgentStepKind.Input;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            var scan = await SelectionRules.ScanAsync(uow, ct);
            return scan.Where(x => x.Decision.Kind == SelectionKind.Manual)
                .Select(x => Item("PurchaseInquiry", x.Inquiry.Id, x.Inquiry.Number, x.Inquiry.PurchaseRequest.ContractId, x.Inquiry.PurchaseRequest.WorkOrderId, "ایجنت انتخاب نکرد: " + x.Decision.Reason)).ToList();
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo { Roles = ProcurementRoles.Commercial, Link = InquiryText.Link(item), Title = "انتخاب دستی برنده استعلام " + item.TargetNumber };
    }

    /// <summary>PROC.INQ.COMMITTEE: the selection committee (financial, commercial, technical from the committee limit) and the managing director where the value or the quotes ask for it. The roles are the ones still missing.</summary>
    internal sealed class CommitteeGateStep : ProcStep
    {
        public override string Code => "PROC.INQ.COMMITTEE";
        public override string AgentCode => AgentCodes.Proc;
        public override string Title => "تایید انتخاب تامین‌کننده توسط کمیته";
        public override AgentStepKind Kind => AgentStepKind.HumanGate;

        public override async Task<List<AgentWorkItem>> DiscoverAsync(IUnitOfWork uow, DateTime today, CancellationToken ct)
        {
            Dictionary<string, decimal> s = await ProcStepCommon.SettingsAsync(uow, ct);
            List<PurchaseInquiry> list = await uow.Set<PurchaseInquiry>().AsNoTracking().Include(i => i.Quotes).Include(i => i.PurchaseRequest)
                .Where(i => !i.Deleted && i.Status == InquiryStatus.Selected).OrderBy(i => i.Id).Take(ProcStepCommon.Cap).ToListAsync(ct);
            var items = new List<AgentWorkItem>();
            foreach (PurchaseInquiry i in list)
            {
                SupplierQuote chosen = i.Quotes.FirstOrDefault(q => q.Id == i.SelectedQuoteId);
                decimal amount = chosen?.TotalAmount ?? 0;
                // the same list as the inbox of the inquiry (ProcurementService.SyncInquiryGateAsync)
                var roles = new List<string>();
                if (amount >= s[ProcurementSettingKeys.CommitteeFrom])
                {
                    if (!i.FinancialApprovedAt.HasValue) roles.Add(ProcurementRoles.Finance);
                    if (!i.CommercialApprovedAt.HasValue) roles.Add(ProcurementRoles.Commercial);
                    if (!i.TechnicalApprovedAt.HasValue) roles.Add(ProcurementRoles.Technical);
                }
                if (ProcurementAuthority.InquiryNeedsManagement(s, i, i.PurchaseRequest, amount) && !i.ManagementApprovedAt.HasValue) roles.Add(ProcurementRoles.Management);
                string joined = ProcStepCommon.Roles(roles.ToArray());
                if (joined == null) continue;
                items.Add(Item("PurchaseInquiry", i.Id, i.Number, i.PurchaseRequest.ContractId, i.PurchaseRequest.WorkOrderId, $"برنده انتخاب شد ({ProcStepCommon.Num(amount)} ریال) و تایید کمیته یا مدیرعامل لازم است.", joined));
            }
            return items;
        }

        public override AgentGateInfo GateInfo(AgentWorkItem item) => new AgentGateInfo
        {
            Roles = item.GateRoles ?? ProcStepCommon.Roles(ProcurementRoles.Finance, ProcurementRoles.Commercial, ProcurementRoles.Technical, ProcurementRoles.Management), Link = InquiryText.Link(item),
            Title = "تایید انتخاب تامین‌کننده استعلام " + item.TargetNumber, HasOfficeInboxItem = true,
        };
    }
}
