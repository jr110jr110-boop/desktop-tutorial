using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Services.NasbNiro.Ai;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Inventory;
using ViewModels.NasbNiro.AiDtos;
using ViewModels.NasbNiro.CeoDtos;
using ViewModels.NasbNiro.FinanceDtos;

namespace Services.NasbNiro.Ceo.Drills
{
    /// <summary>
    /// The numbers of one section of the CEO dashboard (or the headline numbers of all sections) as one object for the AI: Persian titles, values, units and the drill key of each number.
    /// The tool ceo_overview and the analysis both read them here. The implementation of release D1 part A2 is provisional: it maps the existing CEO dashboard;
    /// the orchestrator switches it to the real overview of part A1.
    /// </summary>
    public interface ICeoSectionNumbersProvider
    {
        /// <param name="sectionKey">One of <see cref="CeoModules.Sections"/>; null or empty for the headline numbers of all sections.</param>
        Task<object> GetAsync(string sectionKey, CancellationToken ct);
    }

    /// <summary>One number of a section.</summary>
    public sealed class CeoNumberDto
    {
        public string Key { get; set; }
        public string Title { get; set; }

        /// <summary>Empty when the number is hidden for the caller's role (payroll privacy) or does not exist yet.</summary>
        public decimal? Value { get; set; }

        /// <summary>rial, count, percent, days, hours or score.</summary>
        public string Unit { get; set; }

        /// <summary>The drill that lists the records behind the number (a key of the catalogue).</summary>
        public string DrillKey { get; set; }
        public bool? Hidden { get; set; }
    }

    public sealed class CeoSectionNumbersDto
    {
        public string Section { get; set; }
        public string Title { get; set; }
        public string AsOfText { get; set; }
        public List<CeoNumberDto> Items { get; set; } = new List<CeoNumberDto>();
    }

    public sealed class CeoOverviewNumbersDto
    {
        public string AsOfText { get; set; }
        public List<CeoSectionNumbersDto> Sections { get; set; } = new List<CeoSectionNumbersDto>();
    }

    /// <summary>The provisional provider: the numbers of the existing CEO dashboard, grouped by the nine sections.</summary>
    public class CeoDashboardSectionNumbersProvider : ICeoSectionNumbersProvider
    {
        /// <summary>How many numbers of a section the headline of all sections shows.</summary>
        public const int HeadlinePerSection = 4;

        private readonly ICeoDashboardService _dashboard;
        private readonly IInventoryUserContext _user;

        public CeoDashboardSectionNumbersProvider(ICeoDashboardService dashboard, IInventoryUserContext user)
        {
            _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        public async Task<object> GetAsync(string sectionKey, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoDrillRoles.Readers)) throw new AiToolForbiddenException();
            string section = DrillText.Clean(sectionKey)?.ToLowerInvariant();
            if (section != null && !CeoModules.Sections.Contains(section))
                throw new AiToolFailureException("بخش ناشناخته است؛ یکی از این‌ها را بدهید: " + string.Join("، ", CeoModules.Sections));
            CeoDashboardDto d = AiToolShaper.Unwrap(await _dashboard.GetAsync(ct));
            string asOf = DrillDates.Text(d.GeneratedAt.LocalDateTime);
            if (section != null) return Build(section, d, asOf, int.MaxValue);
            return new CeoOverviewNumbersDto { AsOfText = asOf, Sections = CeoModules.Sections.Select(s => Build(s, d, asOf, HeadlinePerSection)).ToList() };
        }

        private static CeoSectionNumbersDto Build(string section, CeoDashboardDto d, string asOf, int take) => new CeoSectionNumbersDto
        {
            Section = section,
            Title = CeoModules.Title(section),
            AsOfText = take == int.MaxValue ? asOf : null,
            Items = Numbers(section, d).Take(take).ToList(),
        };

        private static CeoNumberDto N(string key, string title, decimal? value, string unit, string drillKey, bool hidden = false) =>
            new CeoNumberDto { Key = key, Title = title, Value = hidden ? null : value, Unit = unit, DrillKey = drillKey, Hidden = hidden ? true : (bool?)null };

        /// <summary>The numbers of a section, the most important first (the headline of all sections takes the first ones).</summary>
        public static IEnumerable<CeoNumberDto> Numbers(string section, CeoDashboardDto d)
        {
            switch (section)
            {
                case CeoModules.Finance:
                    yield return N("cashAndBank", "نقد و بانک", d.Finance.CashAndBank, "rial", "fin.cash_accounts");
                    yield return N("receivablesOpen", "مطالبات باز", d.Finance.ReceivablesOpen, "rial", "fin.receivables_by_customer");
                    yield return N("receivablesOverdue", "مطالبات سررسیدگذشته", d.Finance.ReceivablesOverdue, "rial", "fin.receivable_invoices_overdue");
                    yield return N("payablesOpen", "بدهی باز به تأمین‌کنندگان", d.PayablesOpen, "rial", "fin.payables_by_supplier");
                    yield return N("payablesDue30", "بدهی سررسید ۳۰ روز آینده", d.Finance.PayablesDue30, "rial", "fin.payables_overdue");
                    yield return N("issuedChequesDue7Count", "چک‌های صادرشده با سررسید ۷ روز آینده", d.Finance.IssuedChequesDue7Count, "count", "fin.cheques_due");
                    yield return N("netProfitYtd", "سود خالص از ابتدای سال", d.Finance.NetProfitYtd, "rial", "fin.revenue_by_month");
                    break;
                case CeoModules.Sales:
                    yield return N("salesPipelineValue", "ارزش خط لوله‌ی فروش", d.SalesPipelineValue, "rial", "sl.pipeline_by_stage");
                    yield return N("salesOpenInquiries", "استعلام‌های باز", d.SalesOpenInquiries, "count", "sl.inquiries_open");
                    yield return N("salesHitRatePercent", "نرخ برد", d.SalesHitRatePercent, "percent", "sl.hit_rate_by_month");
                    yield return N("salesOrdersWaitingCredit", "سفارش‌های منتظر اعتبار", d.SalesOrdersWaitingCredit, "count", "sl.orders_waiting_credit");
                    yield return N("salesProposalsWaitingApproval", "پیشنهادهای منتظر تأیید", d.SalesProposalsWaitingApproval, "count", "sl.proposals_waiting");
                    yield return N("salesProposalsDueSoon", "پیشنهادهای نزدیک به مهلت", d.SalesProposalsDueSoon, "count", "sl.proposals_due");
                    yield return N("salesBidDecisionsWaiting", "تصمیم شرکت در مناقصه‌ی منتظر", d.SalesBidDecisionsWaiting, "count", "sl.inquiries_open");
                    break;
                case CeoModules.Projects:
                    yield return N("pcActiveProjects", "پروژه‌های فعال", d.PcActiveProjects, "count", "pc.portfolio");
                    yield return N("pcProjectsRed", "پروژه‌های قرمز", d.PcProjectsRed, "count", "pc.portfolio");
                    yield return N("pcLateActivities", "فعالیت‌های دارای تأخیر", d.PcLateActivities, "count", "pc.late_activities");
                    yield return N("pcOverdueMilestones", "نقاط عطف عقب‌افتاده", d.PcOverdueMilestones, "count", "pc.milestones_next");
                    yield return N("pcOpenChangeRequests", "درخواست‌های تغییر باز", d.PcOpenChangeRequests, "count", "pc.change_requests_open");
                    yield return N("pcGuaranteeAlerts", "ضمانت‌نامه‌های نزدیک به سررسید", d.PcGuaranteeAlerts, "count", "pc.guarantees_expiring");
                    yield return N("pcPenaltyExposure", "مواجهه‌ی جریمه‌ی تأخیر", d.PcPenaltyExposure, "rial", "pc.penalty_exposure");
                    yield return N("pcProjectsAmber", "پروژه‌های زرد", d.PcProjectsAmber, "count", "pc.portfolio");
                    yield return N("pcOverdueInitiationSteps", "گام‌های عقب‌افتاده‌ی شروع پروژه", d.PcOverdueInitiationSteps, "count", "pc.initiation_overdue");
                    yield return N("pcOverdueClientResponses", "پاسخ‌های عقب‌افتاده‌ی کارفرما", d.PcOverdueClientResponses, "count", "pc.client_responses_overdue");
                    break;
                case CeoModules.Production:
                    yield return N("productionLateOrders", "دستورهای تولید دارای تأخیر", d.ProductionLateOrders, "count", "mf.orders_late");
                    yield return N("productionMachinesDown", "ماشین‌های خراب", d.ProductionMachinesDown, "count", "mf.machines_down");
                    yield return N("productionMaintenanceOverdue", "نگهداری سررسیدشده", d.ProductionMaintenanceOverdue, "count", "mf.maintenance_overdue");
                    yield return N("productionOverloadWeeks", "هفته‌های بیش از ۱۱۰ درصد ظرفیت در چهار هفته‌ی آینده", d.ProductionOverloadWeeks, "count", "mf.work_center_load");
                    break;
                case CeoModules.Quality:
                    yield return N("openNcrs", "گزارش‌های عدم انطباق باز", d.OpenNcrs, "count", "qc.ncr_open");
                    yield return N("overdueCapas", "اقدام اصلاحی و پیشگیرانه‌ی عقب‌افتاده", d.OverdueCapas, "count", "qc.capa_overdue");
                    yield return N("calibrationOverdue", "کالیبراسیون عقب‌افتاده", d.CalibrationOverdue, "count", "qc.calibration_due");
                    yield return N("complaintsOverdue", "شکایت‌های عقب‌افتاده", d.ComplaintsOverdue, "count", "qc.complaints_open");
                    yield return N("pendingHPoints", "نقاط توقف (H) منتظر", d.PendingHPoints, "count", "qc.h_points_pending");
                    yield return N("documentsWaitingApproval", "اسناد منتظر تأیید", d.DocumentsWaitingApproval, "count", "qc.documents_waiting");
                    yield return N("calibrationDue30", "کالیبراسیون سررسید ۳۰ روز آینده", d.CalibrationDue30, "count", "qc.calibration_due");
                    break;
                case CeoModules.Inventory:
                    yield return N("stockValue", "ارزش موجودی", d.StockValue, "rial", "inv.stock_value_by_warehouse");
                    yield return N("itemsBelowMinimum", "کالاهای زیر حداقل موجودی", d.ItemsBelowMinimum, "count", "inv.items_below_minimum");
                    yield return N("quarantineLines", "ردیف‌های قرنطینه", d.QuarantineLines, "count", "inv.quarantine");
                    yield return N("openMaterialRequests", "درخواست‌های مواد باز", d.OpenMaterialRequests, "count", "inv.material_requests_open");
                    break;
                case CeoModules.Procurement:
                    yield return N("openPurchaseOrders", "سفارش‌های خرید باز", d.OpenPurchaseOrders, "count", "pr.orders_open");
                    yield return N("openPurchaseOrdersAmount", "مبلغ سفارش‌های خرید باز", d.OpenPurchaseOrdersAmount, "rial", "pr.orders_open");
                    yield return N("lateOrderLines", "ردیف‌های سفارش دارای تأخیر", d.LateOrderLines, "count", "pr.late_lines");
                    yield return N("openPurchaseRequests", "درخواست‌های خرید باز", d.OpenPurchaseRequests, "count", "pr.requests_open");
                    yield return N("invoicesWaitingApproval", "فاکتورهای منتظر تأیید", d.InvoicesWaitingApproval, "count", "pr.invoices_waiting");
                    yield return N("paymentsWaitingApproval", "پرداخت‌های منتظر تأیید", d.PaymentsWaitingApproval, "count", "pr.payments_waiting");
                    yield return N("paymentsWaitingAmount", "مبلغ پرداخت‌های منتظر", d.PaymentsWaitingAmount, "rial", "pr.payments_waiting");
                    yield return N("blockedSuppliers", "تأمین‌کنندگان مسدود", d.BlockedSuppliers, "count", "pr.blocked_suppliers");
                    yield return N("receivedNotInvoiced", "دریافت‌شده‌ی بدون فاکتور", d.ReceivedNotInvoiced, "rial", "pr.invoices_waiting");
                    break;
                case CeoModules.Service:
                    yield return N("serviceOpenRequests", "درخواست‌های خدمات باز", d.ServiceOpenRequests, "count", "sv.requests_open");
                    yield return N("serviceSlaBreaches", "نقض SLA", d.ServiceSlaBreaches, "count", "sv.sla_breaches");
                    yield return N("serviceUnitsInWarranty", "واحدهای در گارانتی", d.ServiceUnitsInWarranty, "count", "sv.units_by_customer");
                    yield return N("serviceWarrantyCostYtd", "هزینه‌ی گارانتی از ابتدای سال", d.ServiceWarrantyCostYtd, "rial", "sv.warranty_cost_by_month", d.ServiceWarrantyCostHidden);
                    yield return N("serviceGoodwillWaiting", "درخواست‌های حسن نیت منتظر", d.ServiceGoodwillWaiting, "count", "sv.orders_open");
                    yield return N("serviceSurveyAverageScore", "میانگین رضایت مشتری", d.ServiceSurveyAverageScore, "score", "sv.survey_by_month");
                    break;
                case CeoModules.Office:
                    yield return N("overdueDecisions", "مصوبات معوق", d.OverdueDecisions, "count", "of.decisions_overdue");
                    break;
            }
        }
    }

    public interface ICeoAnalysisService
    {
        /// <summary>The narrative analysis of a section (or of the company) with verified numbers (CEO and PMO).</summary>
        Task<ResponseDto<AiChatResponseDto>> AnalyzeAsync(CeoAnalyzeDto dto, CancellationToken ct);
    }

    /// <summary>
    /// POST api/v1/ceo/analyze: the existing chat loop as the signed-in user with the prompt ANALYSIS. The numbers of the section (the tool ceo_overview) are read before the first model
    /// turn and put into the conversation as a tool result, so the verifier of the chat accepts exactly those numbers (and those of the drills the model asks for).
    /// </summary>
    public class CeoAnalysisService : ICeoAnalysisService
    {
        private readonly IInventoryUserContext _user;
        private readonly IAiAnalysisRunner _runner;

        public CeoAnalysisService(IInventoryUserContext user, IAiAnalysisRunner runner)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        /// <summary>The question that is asked when the CEO presses the analysis button without a question.</summary>
        public static string DefaultQuestion(string section) => section == null
            ? "وضعیت کل شرکت را تحلیل کن؛ با دوره‌ی قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌های اصلی را بگو."
            : "وضعیت بخش «" + CeoModules.Title(section) + "» را تحلیل کن؛ با دوره‌ی قبل مقایسه کن و سه نکته‌ی مهم و ریسک‌های اصلی را بگو.";

        public async Task<ResponseDto<AiChatResponseDto>> AnalyzeAsync(CeoAnalyzeDto dto, CancellationToken ct)
        {
            if (!_user.IsInRole(CeoDrillRoles.Readers)) return ResponseDtoHelper.BadRequest<AiChatResponseDto>(CeoDrillText.NotAllowed);
            string section = DrillText.Clean(dto?.SectionKey)?.ToLowerInvariant();
            if (section == "all") section = null;
            if (section != null && !CeoModules.Sections.Contains(section))
                return ResponseDtoHelper.BadRequest<AiChatResponseDto>("بخش ناشناخته است؛ یکی از این‌ها را بدهید: " + string.Join("، ", CeoModules.Sections));

            var request = new AiAnalysisRequest
            {
                SectionKey = section,
                Question = DrillText.Clean(dto?.Question) ?? DefaultQuestion(section),
                ConversationId = dto?.ConversationId,
            };
            request.PreCalls.Add(new AiPreCall { Tool = AiCeoTools.OverviewTool, ArgumentsJson = section == null ? "{}" : "{\"section\":\"" + section + "\"}" });
            return await _runner.RunAnalysisAsync(request, ct);
        }
    }
}
