using System;
using System.Collections.Generic;
using System.Linq;
using Services.NasbNiro.Finance;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ceo
{
    /// <summary>
    /// What a KPI is before it has a value: the section it belongs to, its key, title, unit, front link, drill key and drill parameters.
    /// The overview builds every KPI from its spec, so there is one definition; a section that could not be computed returns its specs as placeholders
    /// (value null, status neutral) and keeps the shape of the contract.
    /// </summary>
    public sealed class CeoKpiSpec
    {
        public string Section { get; init; }
        public string Key { get; init; }
        public string Title { get; init; }
        public string Unit { get; init; }
        public string Link { get; init; }
        public string DrillKey { get; init; }

        /// <summary>The drill parameters on a given day (null when the drill needs none).</summary>
        public Func<DateTime, Dictionary<string, string>> DrillParams { get; init; }
    }

    /// <summary>The KPIs of the nine sections, in display order (48 in all). The headline of the overview takes eight of them by key.</summary>
    public static class CeoKpiSpecs
    {
        private static CeoKpiSpec S(string section, string key, string title, string unit, string link, string drillKey, Func<DateTime, Dictionary<string, string>> drillParams = null) =>
            new CeoKpiSpec { Section = section, Key = key, Title = title, Unit = unit, Link = link, DrillKey = drillKey, DrillParams = drillParams };

        private static Func<DateTime, Dictionary<string, string>> Only(string name, string value) => _ => new Dictionary<string, string> { [name] = value };

        /// <summary>The fiscal year so far: from the first day of the Persian year to today (the period of the profit of the year).</summary>
        private static Dictionary<string, string> YearToDate(DateTime today) => new Dictionary<string, string>
        {
            ["from"] = CeoText.Iso(FinanceSeed.YearStart(FinanceSeed.PersianYearOf(today))), ["to"] = CeoText.Iso(today.Date),
        };

        public static readonly IReadOnlyList<CeoKpiSpec> All = new List<CeoKpiSpec>
        {
            // finance
            S(CeoKeys.Finance, CeoKeys.Cash, "نقد و بانک", CeoUnit.Rial, CeoLinks.CashPosition, CeoDrillRef.FinCashAccounts),
            S(CeoKeys.Finance, CeoKeys.ReceivablesOpen, "مطالبات باز", CeoUnit.Rial, CeoLinks.ReceivablesAgeing, CeoDrillRef.FinReceivablesByCustomer),
            S(CeoKeys.Finance, CeoKeys.PayablesOverdue, "بدهی سررسیدگذشته به تأمین‌کنندگان", CeoUnit.Rial, CeoLinks.TreasuryPayments, CeoDrillRef.FinPayablesOverdue),
            S(CeoKeys.Finance, "issuedChequesDue7", "چک‌های صادره با سررسید هفت روز آینده", CeoUnit.Rial, CeoLinks.Cheques, CeoDrillRef.FinChequesDue),
            S(CeoKeys.Finance, "netProfitYtd", "سود خالص از ابتدای سال مالی", CeoUnit.Rial, CeoLinks.IncomeStatement, CeoDrillRef.FinRevenueByMonth, YearToDate),
            S(CeoKeys.Finance, "vouchersWaiting", "اسناد حسابداری منتظر بررسی یا قطعی‌شدن", CeoUnit.Count, CeoLinks.Vouchers, CeoDrillRef.FinVouchersWaiting),

            // sales
            S(CeoKeys.Sales, CeoKeys.SalesBacklog, "سفارش‌های در جریان (backlog)", CeoUnit.Rial, CeoLinks.SalesOrders, CeoDrillRef.SlOrdersOpen), // the drill lists the open orders by itself (it declares no status parameter)
            S(CeoKeys.Sales, CeoKeys.SalesPipelineValue, "ارزش خط لوله‌ی فروش", CeoUnit.Rial, CeoLinks.SalesDashboard, CeoDrillRef.SlPipelineByStage),
            S(CeoKeys.Sales, "salesHitRate", "نرخ برد پیشنهادها", CeoUnit.Percent, CeoLinks.SalesProposals, CeoDrillRef.SlHitRateByMonth),
            S(CeoKeys.Sales, "salesProposalsDueSoon", "پیشنهادهای نزدیک به مهلت ارسال", CeoUnit.Count, CeoLinks.SalesInquiries, CeoDrillRef.SlProposalsDue),
            S(CeoKeys.Sales, "salesProposalsWaiting", "پیشنهادهای منتظر تأیید", CeoUnit.Count, CeoLinks.SalesProposals, CeoDrillRef.SlProposalsWaiting),
            S(CeoKeys.Sales, "salesOrdersWaitingCredit", "سفارش‌های منتظر تأیید اعتبار", CeoUnit.Count, CeoLinks.SalesOrders, CeoDrillRef.SlOrdersWaitingCredit),

            // projects
            S(CeoKeys.Projects, CeoKeys.ActiveProjects, "پروژه‌های فعال", CeoUnit.Count, CeoLinks.Portfolio, CeoDrillRef.PcPortfolio),
            S(CeoKeys.Projects, "projectsRed", "پروژه‌های قرمز", CeoUnit.Count, CeoLinks.Portfolio, CeoDrillRef.PcPortfolio, Only("status", "danger")),
            S(CeoKeys.Projects, "projectsAmber", "پروژه‌های زرد", CeoUnit.Count, CeoLinks.Portfolio, CeoDrillRef.PcPortfolio, Only("status", "warn")),
            S(CeoKeys.Projects, "pcLateActivities", "فعالیت‌های دارای تأخیر", CeoUnit.Count, CeoLinks.PcActivities, CeoDrillRef.PcLateActivities),
            S(CeoKeys.Projects, "pcPenaltyExposure", "مواجهه‌ی جریمه‌ی تأخیر", CeoUnit.Rial, CeoLinks.PcPenalties, CeoDrillRef.PcPenaltyExposure),
            S(CeoKeys.Projects, "pcGuaranteeAlerts", "ضمانت‌نامه‌های نزدیک به سررسید", CeoUnit.Count, CeoLinks.PcGuarantees, CeoDrillRef.PcGuaranteesExpiring),

            // production
            S(CeoKeys.Production, CeoKeys.LateProductionOrders, "دستورهای تولید دارای تأخیر", CeoUnit.Count, CeoLinks.ProductionOrders, CeoDrillRef.MfOrdersLate),
            S(CeoKeys.Production, "machinesDown", "ماشین‌های خراب", CeoUnit.Count, CeoLinks.ProductionMaintenance, CeoDrillRef.MfMachinesDown),
            S(CeoKeys.Production, "maintenanceOverdue", "نگهداری سررسیدگذشته", CeoUnit.Count, CeoLinks.ProductionMaintenance, CeoDrillRef.MfMaintenanceOverdue),
            S(CeoKeys.Production, "overloadWeeks", "هفته‌های بیش از ۱۱۰٪ ظرفیت", CeoUnit.Count, CeoLinks.ProductionSchedule, CeoDrillRef.MfWorkCenterLoad),
            S(CeoKeys.Production, "oee", "بهره‌وری کلی تجهیزات (OEE)", CeoUnit.Percent, CeoLinks.ProductionOee, CeoDrillRef.MfOeeByWorkCenter),

            // quality
            S(CeoKeys.Quality, CeoKeys.OpenNcrs, "گزارش‌های عدم انطباق باز", CeoUnit.Count, CeoLinks.Ncr, CeoDrillRef.QcNcrOpen),
            S(CeoKeys.Quality, "overdueNcrs", "عدم انطباق‌های گذشته از مهلت تصمیم", CeoUnit.Count, CeoLinks.Ncr, CeoDrillRef.QcNcrOpen),
            S(CeoKeys.Quality, "overdueCapas", "اقدام‌های اصلاحی و پیشگیرانه‌ی عقب‌افتاده", CeoUnit.Count, CeoLinks.Capa, CeoDrillRef.QcCapaOverdue),
            S(CeoKeys.Quality, "calibrationOverdue", "تجهیزات اندازه‌گیری با کالیبراسیون سررسیدگذشته", CeoUnit.Count, CeoLinks.Calibration, CeoDrillRef.QcCalibrationDue),
            S(CeoKeys.Quality, "complaintsOpen", "شکایت‌های باز مشتریان", CeoUnit.Count, CeoLinks.Complaints, CeoDrillRef.QcComplaintsOpen),
            S(CeoKeys.Quality, "pendingHPoints", "نقاط توقف (H) منتظر رهاسازی", CeoUnit.Count, CeoLinks.QualityDashboard, CeoDrillRef.QcHPointsPending),

            // inventory
            S(CeoKeys.Inventory, CeoKeys.StockValue, "ارزش موجودی انبار", CeoUnit.Rial, CeoLinks.Stock, CeoDrillRef.InvStockValueByWarehouse),
            S(CeoKeys.Inventory, CeoKeys.ItemsBelowMinimum, "کالاهای زیر حداقل موجودی", CeoUnit.Count, CeoLinks.Stock, CeoDrillRef.InvItemsBelowMinimum),
            S(CeoKeys.Inventory, "quarantineLines", "ردیف‌های موجود در قرنطینه", CeoUnit.Count, CeoLinks.Warehouses, CeoDrillRef.InvQuarantine),
            S(CeoKeys.Inventory, "openMaterialRequests", "درخواست‌های مواد منتظر حواله", CeoUnit.Count, CeoLinks.MaterialRequests, CeoDrillRef.InvMaterialRequestsOpen),

            // procurement
            S(CeoKeys.Procurement, CeoKeys.OpenPurchaseOrdersAmount, "ارزش سفارش‌های خرید باز", CeoUnit.Rial, CeoLinks.ProcurementOrders, CeoDrillRef.PrOrdersOpen),
            S(CeoKeys.Procurement, "lateOrderLines", "ردیف‌های سفارش خرید دارای تأخیر", CeoUnit.Count, CeoLinks.ProcurementOrders, CeoDrillRef.PrLateLines),
            S(CeoKeys.Procurement, "purchaseRequestsWaiting", "درخواست‌های خرید منتظر تأیید", CeoUnit.Count, CeoLinks.ProcurementRequests, CeoDrillRef.PrRequestsOpen),
            S(CeoKeys.Procurement, "invoicesWaiting", "فاکتورهای تأمین‌کننده منتظر تأیید", CeoUnit.Count, CeoLinks.ProcurementInvoices, CeoDrillRef.PrInvoicesWaiting),
            S(CeoKeys.Procurement, "paymentsWaiting", "درخواست‌های پرداخت منتظر تأیید یا پرداخت", CeoUnit.Count, CeoLinks.ProcurementPayments, CeoDrillRef.PrPaymentsWaiting),
            S(CeoKeys.Procurement, "blockedSuppliers", "تأمین‌کنندگان مسدود", CeoUnit.Count, CeoLinks.ProcurementSuppliers, CeoDrillRef.PrBlockedSuppliers),

            // after-sales service (the title of the SLA figure keeps the Latin acronym in brackets: it is read to the CEO without it)
            S(CeoKeys.Service, CeoKeys.ServiceSlaBreaches, "درخواست‌های خارج از مهلت (SLA)", CeoUnit.Count, CeoLinks.ServiceRequests, CeoDrillRef.SvSlaBreaches),
            S(CeoKeys.Service, "serviceOpenRequests", "درخواست‌های خدمات باز", CeoUnit.Count, CeoLinks.ServiceRequests, CeoDrillRef.SvRequestsOpen),
            S(CeoKeys.Service, "serviceUnitsInWarranty", "تجهیزات نصب‌شده در دوره‌ی گارانتی", CeoUnit.Count, CeoLinks.ServiceUnits, CeoDrillRef.SvUnitsByCustomer),
            S(CeoKeys.Service, "serviceWarrantyCostYtd", "هزینه‌ی گارانتی از ابتدای سال", CeoUnit.Rial, CeoLinks.ServiceOrders, CeoDrillRef.SvWarrantyCostByMonth, YearToDate),
            S(CeoKeys.Service, "serviceGoodwillWaiting", "درخواست‌های حسن نیت منتظر تصمیم", CeoUnit.Count, CeoLinks.ServiceOrders, CeoDrillRef.SvOrdersOpen),
            S(CeoKeys.Service, CeoKeys.ServiceSurveyScore, "میانگین رضایت مشتریان", CeoUnit.Count, CeoLinks.ServiceSurveys, CeoDrillRef.SvSurveyByMonth),

            // office
            S(CeoKeys.Office, "overdueDecisions", "مصوبات جلسه‌ی معوق", CeoUnit.Count, CeoLinks.OfficeDecisions, CeoDrillRef.OfDecisionsOverdue),
            S(CeoKeys.Office, "lettersWaiting", "نامه‌های منتظر تأیید و امضا", CeoUnit.Count, CeoLinks.OfficeLetters, CeoDrillRef.OfLettersWaiting),
            S(CeoKeys.Office, "leaveRequestsPending", "درخواست‌های مرخصی و ماموریت منتظر تصمیم", CeoUnit.Count, CeoLinks.OfficeLeaves, CeoDrillRef.OfLeaveRequestsPending),
        };

        /// <summary>The spec of a KPI key; an unknown key is a programming error.</summary>
        public static CeoKpiSpec Get(string key) =>
            All.FirstOrDefault(s => s.Key == key) ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown KPI key.");

        /// <summary>The specs of one section, in display order.</summary>
        public static IEnumerable<CeoKpiSpec> Of(string section) => All.Where(s => s.Section == section);

        /// <summary>The KPI of a spec without a value: the shape of a figure that could not be computed.</summary>
        public static CeoKpiDto Placeholder(CeoKpiSpec spec, DateTime today) => new CeoKpiDto
        {
            Key = spec.Key, Title = spec.Title, Value = null, Unit = spec.Unit, ValueText = CeoText.NoValue, Status = CeoStatus.Neutral,
            Link = spec.Link, DrillKey = spec.DrillKey, DrillParams = spec.DrillParams?.Invoke(today.Date),
        };
    }
}
