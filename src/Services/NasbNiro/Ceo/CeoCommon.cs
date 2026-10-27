using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ViewModels.NasbNiro.CeoDtos;

namespace Services.NasbNiro.Ceo
{
    // CEO executive dashboard (release D1, part A1): roles, texts, status rules and the keys the overview shares with the drills, the snapshots and the front.

    public static class CeoRoles
    {
        /// <summary>Who opens the overview, a section and takes a snapshot: the CEO and the PMO.</summary>
        public const string Readers = "CEO,PMO";
    }

    /// <summary>Configuration keys of the CEO dashboard.</summary>
    public static class CeoConfigKeys
    {
        /// <summary>The nightly snapshot job (and its catch-up at start); "false" switches it off.</summary>
        public const string Jobs = "Ceo:Jobs";

        /// <summary>Seconds the text-to-speech call of a spoken summary may take before the answer is returned as text only (degraded); default 20.</summary>
        public const string SpeechTimeoutSeconds = "Ceo:SpeechTimeoutSeconds";
    }

    /// <summary>The fixed keys of the overview (the front and the tests rely on this order).</summary>
    public static class CeoKeys
    {
        public const string Cash = "cash";
        public const string ReceivablesOpen = "receivablesOpen";
        public const string PayablesOverdue = "payablesOverdue";
        public const string SalesBacklog = "salesBacklog";
        public const string ActiveProjects = "activeProjects";
        public const string LateProductionOrders = "lateProductionOrders";
        public const string OpenNcrs = "openNcrs";
        public const string ServiceSlaBreaches = "serviceSlaBreaches";

        public const string StockValue = "stockValue";
        public const string ItemsBelowMinimum = "itemsBelowMinimum";
        public const string OpenPurchaseOrdersAmount = "openPurchaseOrdersAmount";
        public const string SalesPipelineValue = "salesPipelineValue";

        /// <summary>The customer satisfaction KPI of the service section (its spoken form has one decimal).</summary>
        public const string ServiceSurveyScore = "serviceSurveyScore";

        public const string Finance = "finance";
        public const string Sales = "sales";
        public const string Projects = "projects";
        public const string Production = "production";
        public const string Quality = "quality";
        public const string Inventory = "inventory";
        public const string Procurement = "procurement";
        public const string Service = "service";
        public const string Office = "office";

        /// <summary>The eight headline figures, in display order.</summary>
        public static readonly string[] Headline =
        {
            Cash, ReceivablesOpen, PayablesOverdue, SalesBacklog, ActiveProjects, LateProductionOrders, OpenNcrs, ServiceSlaBreaches,
        };

        /// <summary>The nine sections, in display order.</summary>
        public static readonly string[] Sections = { Finance, Sales, Projects, Production, Quality, Inventory, Procurement, Service, Office };

        /// <summary>The figures the nightly job stores (the headline ones and the four the sections draw trends of).</summary>
        public static readonly string[] Snapshot =
        {
            Cash, ReceivablesOpen, PayablesOverdue, SalesBacklog, ActiveProjects, LateProductionOrders, OpenNcrs, ServiceSlaBreaches,
            StockValue, ItemsBelowMinimum, OpenPurchaseOrdersAmount, SalesPipelineValue,
        };
    }

    /// <summary>Front routes (they exist in the router of the front) that show the records behind a figure.</summary>
    public static class CeoLinks
    {
        /// <summary>The CEO dashboard itself.</summary>
        public const string Overview = "/ceo";

        public const string FinanceDashboard = "/fn/dashboard";
        public const string CashPosition = "/fn/treasury/cash-position";
        public const string ReceivablesAgeing = "/fn/receivables/ageing";
        public const string ReceivableInvoices = "/fn/receivables/invoices";
        public const string Cheques = "/fn/treasury/cheques";
        public const string TreasuryPayments = "/fn/treasury/payments";
        public const string IncomeStatement = "/fn/reports/income-statement";
        public const string Vouchers = "/fn/vouchers";

        public const string SalesDashboard = "/sales/dashboard";
        public const string SalesInquiries = "/sales/inquiries";
        public const string SalesProposals = "/sales/proposals";
        public const string SalesQuotations = "/sales/quotations";
        public const string SalesOrders = "/sales/orders";

        public const string Portfolio = "/project-control/portfolio";
        public const string PcActivities = "/project-control/activities";
        public const string PcMilestones = "/project-control/milestones";
        public const string PcChanges = "/project-control/changes";
        public const string PcGuarantees = "/project-control/guarantees";
        public const string PcPenalties = "/project-control/penalties";

        public const string ProductionDashboard = "/production/dashboard";
        public const string ProductionOrders = "/production/orders";
        public const string ProductionSchedule = "/production/schedule";
        public const string ProductionOee = "/production/oee";
        public const string ProductionMaintenance = "/production/maintenance";

        public const string QualityDashboard = "/quality/dashboard";
        public const string Ncr = "/inventory/ncr";
        public const string Capa = "/quality/capa";
        public const string Calibration = "/quality/calibration";
        public const string Complaints = "/quality/complaints";

        public const string Stock = "/inventory/stock";
        public const string Warehouses = "/inventory/warehouses";
        public const string Kardex = "/inventory/kardex";
        public const string MaterialRequests = "/inventory/material-requests";

        public const string ProcurementRequests = "/procurement/requests";
        public const string ProcurementOrders = "/procurement/orders";
        public const string ProcurementInvoices = "/procurement/invoices";
        public const string ProcurementPayments = "/procurement/payments";
        public const string ProcurementSuppliers = "/procurement/suppliers";

        public const string ServiceDashboard = "/service/dashboard";
        public const string ServiceRequests = "/service/requests";
        public const string ServiceOrders = "/service/orders";
        public const string ServiceUnits = "/service/units";
        public const string ServiceContracts = "/service/contracts";
        public const string ServiceSurveys = "/service/surveys";

        public const string OfficeInbox = "/office/inbox";
        public const string OfficeDecisions = "/office/decisions";
        public const string OfficeLetters = "/office/letters";
        public const string OfficeAttendance = "/office/attendance";
        public const string OfficeLeaves = "/office/leaves";
    }

    /// <summary>Keys of the drill catalogue (api/v1/ceo/drill); the overview only points at them.</summary>
    public static class CeoDrillRef
    {
        public const string FinCashAccounts = "fin.cash_accounts";
        public const string FinReceivablesByCustomer = "fin.receivables_by_customer";
        public const string FinReceivableInvoicesOverdue = "fin.receivable_invoices_overdue";
        public const string FinPayablesBySupplier = "fin.payables_by_supplier";
        public const string FinPayablesOverdue = "fin.payables_overdue";
        public const string FinChequesDue = "fin.cheques_due";
        public const string FinRevenueByMonth = "fin.revenue_by_month";
        public const string FinCostByMonth = "fin.cost_by_month";
        public const string FinProjectProfitability = "fin.project_profitability";
        public const string FinBankUnreconciled = "fin.bank_unreconciled";
        public const string FinVouchersWaiting = "fin.vouchers_waiting";

        public const string SlPipelineByStage = "sl.pipeline_by_stage";
        public const string SlInquiriesOpen = "sl.inquiries_open";
        public const string SlProposalsWaiting = "sl.proposals_waiting";
        public const string SlProposalsDue = "sl.proposals_due";
        public const string SlQuotationsExpiring = "sl.quotations_expiring";
        public const string SlOrdersOpen = "sl.orders_open";
        public const string SlOrdersWaitingCredit = "sl.orders_waiting_credit";
        public const string SlCustomersTop = "sl.customers_top";
        public const string SlHitRateByMonth = "sl.hit_rate_by_month";
        public const string SlLostByReason = "sl.lost_by_reason";

        public const string PcPortfolio = "pc.portfolio";
        public const string PcLateActivities = "pc.late_activities";
        public const string PcMilestonesNext = "pc.milestones_next";
        public const string PcChangeRequestsOpen = "pc.change_requests_open";
        public const string PcGuaranteesExpiring = "pc.guarantees_expiring";
        public const string PcPenaltyExposure = "pc.penalty_exposure";
        public const string PcInitiationOverdue = "pc.initiation_overdue";
        public const string PcClientResponsesOverdue = "pc.client_responses_overdue";

        public const string MfOrdersLate = "mf.orders_late";
        public const string MfOrdersByStatus = "mf.orders_by_status";
        public const string MfWorkCenterLoad = "mf.work_center_load";
        public const string MfMachinesDown = "mf.machines_down";
        public const string MfMaintenanceOverdue = "mf.maintenance_overdue";
        public const string MfOeeByWorkCenter = "mf.oee_by_work_center";

        public const string QcNcrOpen = "qc.ncr_open";
        public const string QcCapaOverdue = "qc.capa_overdue";
        public const string QcCalibrationDue = "qc.calibration_due";
        public const string QcComplaintsOpen = "qc.complaints_open";
        public const string QcHPointsPending = "qc.h_points_pending";
        public const string QcDocumentsWaiting = "qc.documents_waiting";

        public const string InvItemsBelowMinimum = "inv.items_below_minimum";
        public const string InvStockValueByWarehouse = "inv.stock_value_by_warehouse";
        public const string InvSlowMoving = "inv.slow_moving";
        public const string InvTopMovements = "inv.top_movements";
        public const string InvQuarantine = "inv.quarantine";
        public const string InvMaterialRequestsOpen = "inv.material_requests_open";

        public const string PrRequestsOpen = "pr.requests_open";
        public const string PrOrdersOpen = "pr.orders_open";
        public const string PrLateLines = "pr.late_lines";
        public const string PrSupplierOnTime = "pr.supplier_on_time";
        public const string PrSpendBySupplier = "pr.spend_by_supplier";
        public const string PrInvoicesWaiting = "pr.invoices_waiting";
        public const string PrPaymentsWaiting = "pr.payments_waiting";
        public const string PrBlockedSuppliers = "pr.blocked_suppliers";

        public const string SvRequestsOpen = "sv.requests_open";
        public const string SvSlaBreaches = "sv.sla_breaches";
        public const string SvOrdersOpen = "sv.orders_open";
        public const string SvWarrantyCostByMonth = "sv.warranty_cost_by_month";
        public const string SvUnitsByCustomer = "sv.units_by_customer";
        public const string SvSurveyByMonth = "sv.survey_by_month";
        public const string SvLtsaExpiring = "sv.ltsa_expiring";

        public const string OfDecisionsOverdue = "of.decisions_overdue";
        public const string OfLettersWaiting = "of.letters_waiting";
        public const string OfAttendanceMonth = "of.attendance_month";
        public const string OfLeaveRequestsPending = "of.leave_requests_pending";
    }

    /// <summary>Units of a KPI or a chart.</summary>
    public static class CeoUnit
    {
        public const string Rial = "rial";
        public const string Count = "count";
        public const string Percent = "percent";
        public const string Days = "days";
        public const string Hours = "hours";
    }

    /// <summary>Chart kinds.</summary>
    public static class CeoKind
    {
        public const string Line = "line";
        public const string Bar = "bar";
        public const string StackedBar = "stackedBar";
        public const string HBar = "hbar";
        public const string Donut = "donut";
    }

    /// <summary>User-visible texts and the Persian number, rial, percent and month formats of the overview.</summary>
    public static class CeoText
    {
        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string UnknownSection = "بخش داشبورد پیدا نشد.";

        /// <summary>The "what" of a spoken summary is not headline, section, alerts or brief.</summary>
        public const string UnknownSpeech = "نوع خلاصه‌ی صوتی معتبر نیست؛ یکی از headline، section، alerts یا brief را بفرستید.";

        /// <summary>The note of a section whose own figures could not be computed (the other sections are fine).</summary>
        public const string SectionUnavailable = "اطلاعات این بخش اکنون در دسترس نیست؛ بخش‌های دیگر بدون تغییر نمایش داده می‌شوند.";

        /// <summary>The note of every section when the CEO dashboard they are drawn from could not be computed.</summary>
        public const string DashboardUnavailable = "داشبورد مدیرعامل اکنون محاسبه نشد و اطلاعات این بخش در دسترس نیست؛ چند ثانیه‌ی دیگر دوباره امتحان کنید.";

        /// <summary>A spoken summary of a section needs the section key.</summary>
        public const string SpeechNeedsSection = "برای خلاصه‌ی صوتی یک بخش، کلید بخش را بفرستید.";

        /// <summary>Shown instead of a value the reader may not see (payroll privacy).</summary>
        public const string HiddenPayroll = "محرمانه (حقوق)";

        public const string NoValue = "—";

        /// <summary>The Persian months, Farvardin first.</summary>
        public static readonly string[] MonthNames =
        {
            "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
        };

        private static readonly PersianCalendar Calendar = new PersianCalendar();

        private const char PersianZero = '۰';
        private const char ThousandsSeparator = '٬';
        private const char DecimalSeparator = '٫';
        private const string PercentSign = "٪";

        /// <summary>Latin digits, comma and point turned into Persian digits, thousands and decimal separators.</summary>
        public static string Localize(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c >= '0' && c <= '9') chars[i] = (char)(PersianZero + (c - '0'));
                else if (c == ',') chars[i] = ThousandsSeparator;
                else if (c == '.') chars[i] = DecimalSeparator;
            }
            return new string(chars);
        }

        /// <summary>A number with Persian digits and thousands separators, rounded half away from zero to the given decimals (trailing zeros dropped).</summary>
        public static string Number(decimal value, int decimals = 0)
        {
            decimal rounded = Math.Round(value, Math.Max(0, decimals), MidpointRounding.AwayFromZero);
            if (rounded == 0m) return Localize("0");
            string pattern = decimals <= 0 ? "#,##0" : "#,##0." + new string('#', decimals);
            return Localize(rounded.ToString(pattern, CultureInfo.InvariantCulture));
        }

        /// <summary>"۱٬۲۳۴٬۵۶۷ ریال".</summary>
        public static string Rial(decimal value) => Number(value) + " ریال";

        /// <summary>"۱۲٫۵٪" (a percent value, not a ratio).</summary>
        public static string Percent(decimal value, int decimals = 1) => Number(value, decimals) + PercentSign;

        /// <summary>
        /// A rial amount in words: below 1000 as it is ("۹۵۰ ریال"), then "هزار ریال", "میلیون ریال" and "میلیارد ریال" with at most one decimal
        /// ("۴۸٫۲ میلیارد ریال", "۳۵۰ میلیون ریال", "۹۰۰ هزار ریال"). Billions of 1000 and more keep the thousands separator.
        /// </summary>
        public static string ShortRial(decimal value)
        {
            decimal abs = Math.Abs(value);
            if (Math.Round(abs, 0, MidpointRounding.AwayFromZero) == 0m) return Localize("0") + " ریال";
            string sign = value < 0 ? "-" : string.Empty;
            if (abs < 1000m) return sign + Number(abs) + " ریال";

            (decimal Factor, string Word)[] scales = { (1000m, "هزار ریال"), (1000000m, "میلیون ریال"), (1000000000m, "میلیارد ریال") };
            int index = abs < 1000000m ? 0 : abs < 1000000000m ? 1 : 2;
            decimal scaled = Math.Round(abs / scales[index].Factor, 1, MidpointRounding.AwayFromZero);
            if (scaled >= 1000m && index < scales.Length - 1)
            {
                // 999.95 thousand rounds to 1000.0 thousand: say "1 million" instead
                index++;
                scaled = Math.Round(abs / scales[index].Factor, 1, MidpointRounding.AwayFromZero);
            }
            return sign + Number(scaled, 1) + " " + scales[index].Word;
        }

        /// <summary>The full display text of a value in its unit ("—" when there is no value).</summary>
        public static string ValueText(decimal? value, string unit)
        {
            if (!value.HasValue) return NoValue;
            decimal v = value.Value;
            switch (unit)
            {
                case CeoUnit.Rial: return Rial(v);
                case CeoUnit.Percent: return Percent(v);
                case CeoUnit.Days: return Number(v) + " روز";
                case CeoUnit.Hours: return Number(v, 1) + " ساعت";
                default: return Number(v, v == decimal.Truncate(v) ? 0 : 2);
            }
        }

        /// <summary>The short form of a rial amount; null for any other unit or an empty value.</summary>
        public static string ShortText(decimal? value, string unit) => value.HasValue && unit == CeoUnit.Rial ? ShortRial(value.Value) : null;

        /// <summary>The change against the previous month without a sign: "۲٫۱ میلیارد ریال بیشتر از ماه قبل", "۳ مورد کمتر از ماه قبل", "بدون تغییر نسبت به ماه قبل".</summary>
        public static string DeltaText(decimal delta, string unit)
        {
            if (delta == 0m) return "بدون تغییر نسبت به ماه قبل";
            decimal amount = Math.Abs(delta);
            string text;
            switch (unit)
            {
                case CeoUnit.Rial: text = ShortRial(amount); break;
                case CeoUnit.Percent: text = Percent(amount); break;
                case CeoUnit.Days: text = Number(amount) + " روز"; break;
                case CeoUnit.Hours: text = Number(amount, 1) + " ساعت"; break;
                default: text = Number(amount, amount == decimal.Truncate(amount) ? 0 : 2) + " مورد"; break;
            }
            return text + (delta > 0 ? " بیشتر از ماه قبل" : " کمتر از ماه قبل");
        }

        /// <summary>The Persian month and year of a date: "مهر ۱۴۰۵".</summary>
        public static string MonthLabel(DateTime day)
        {
            int month = Calendar.GetMonth(day);
            int year = Calendar.GetYear(day);
            return MonthNames[month - 1] + " " + Localize(year.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The Shamsi date as the other modules print it ("1405/08/03").</summary>
        public static string Date(DateTime day) => string.Format(CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}", Calendar.GetYear(day), Calendar.GetMonth(day), Calendar.GetDayOfMonth(day));

        /// <summary>The ISO date of the JSON and of the drill parameters ("2026-10-25").</summary>
        public static string Iso(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The status rules (★ thresholds). A status is one of ok, warn, danger or neutral; a KPI with no rule is neutral.
    /// </summary>
    public static class CeoRules
    {
        /// <summary>A count: ok below warnAt, warn from warnAt, danger from dangerAt.</summary>
        public static string ByCount(decimal value, decimal warnAt, decimal dangerAt)
        {
            if (value >= dangerAt) return CeoStatus.Danger;
            if (value >= warnAt) return CeoStatus.Warn;
            return CeoStatus.Ok;
        }

        /// <summary>"Any is a problem": ok at zero, warn above zero, danger from dangerAt.</summary>
        public static string ByAny(decimal value, decimal dangerAt) => ByCount(value, 1, dangerAt);

        /// <summary>Ok at zero, warn from one (the module itself raises only a warning for it).</summary>
        public static string WarnIfAny(decimal value) => value >= 1m ? CeoStatus.Warn : CeoStatus.Ok;

        /// <summary>Ok at zero, danger from one (the module itself raises a danger alert for it).</summary>
        public static string DangerIfAny(decimal value) => value >= 1m ? CeoStatus.Danger : CeoStatus.Ok;

        /// <summary>Cash and bank: danger when negative, otherwise ok (the cover of the issued cheques is judged by the cheques figure, see <see cref="IssuedCheques"/>).</summary>
        public static string Cash(decimal cash) => cash < 0m ? CeoStatus.Danger : CeoStatus.Ok;

        /// <summary>A profit and loss figure (net profit of the year): warn when it is a loss, ok otherwise.</summary>
        public static string Profit(decimal netProfit) => netProfit < 0m ? CeoStatus.Warn : CeoStatus.Ok;

        /// <summary>OEE (percent): danger below 40, warn below 60, ok from 60 (the usual world-class reading is 85, a plant starting out sits at 60).</summary>
        public static string Oee(decimal percent) => percent < 40m ? CeoStatus.Danger : percent < 60m ? CeoStatus.Warn : CeoStatus.Ok;

        /// <summary>Hit rate of the sales funnel (percent): warn below 20, ok from 20; never danger (a short history is common).</summary>
        public static string HitRate(decimal percent) => percent < 20m ? CeoStatus.Warn : CeoStatus.Ok;

        /// <summary>Customer satisfaction (survey score out of 5): danger below 3, warn below 4, ok from 4.</summary>
        public static string Satisfaction(decimal score) => score < 3m ? CeoStatus.Danger : score < 4m ? CeoStatus.Warn : CeoStatus.Ok;

        /// <summary>Overdue receivables: ok at zero, warn above zero, danger when they are 30 percent or more of the open receivables.</summary>
        public static string ReceivablesOverdue(decimal overdue, decimal open)
        {
            if (overdue <= 0m) return CeoStatus.Ok;
            return open > 0m && overdue * 100m >= open * 30m ? CeoStatus.Danger : CeoStatus.Warn;
        }

        /// <summary>Overdue payables: ok at zero, warn above zero, danger when they exceed the cash in hand.</summary>
        public static string PayablesOverdue(decimal overdue, decimal cash)
        {
            if (overdue <= 0m) return CeoStatus.Ok;
            return overdue > cash ? CeoStatus.Danger : CeoStatus.Warn;
        }

        /// <summary>Issued cheques: danger when the next seven days exceed the cash, warn when the next thirty days do; ok when nothing falls due, whatever the cash is (a negative cash is judged by <see cref="Cash"/>).</summary>
        public static string IssuedCheques(decimal due7, decimal due30, decimal cash)
        {
            if (due7 > 0m && due7 > cash) return CeoStatus.Danger;
            if (due30 > 0m && due30 > cash) return CeoStatus.Warn;
            return CeoStatus.Ok;
        }

        /// <summary>The projects light: danger with any red project, warn with any amber one.</summary>
        public static string Projects(int red, int amber) => red > 0 ? CeoStatus.Danger : amber > 0 ? CeoStatus.Warn : CeoStatus.Ok;

        /// <summary>A section's light: danger when any KPI is danger, warn when any is warn, ok when at least one KPI has a rule, else neutral.</summary>
        public static string Pulse(IEnumerable<string> statuses)
        {
            List<string> list = statuses?.ToList() ?? new List<string>();
            if (list.Any(s => s == CeoStatus.Danger)) return CeoStatus.Danger;
            if (list.Any(s => s == CeoStatus.Warn)) return CeoStatus.Warn;
            return list.Any(s => s == CeoStatus.Ok) ? CeoStatus.Ok : CeoStatus.Neutral;
        }

        /// <summary>Whether a change is favourable: null for no change or when the figure has no preferred direction.</summary>
        public static bool? DeltaGood(decimal delta, bool? higherIsBetter)
        {
            if (delta == 0m || !higherIsBetter.HasValue) return null;
            return (delta > 0m) == higherIsBetter.Value;
        }
    }
}
