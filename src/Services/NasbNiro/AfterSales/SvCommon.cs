using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Entities.Model.NasbNiro.Sales;
using Microsoft.EntityFrameworkCore;
using Services.NasbNiro.Finance;
using Services.NasbNiro.Sales;

namespace Services.NasbNiro.AfterSales
{
    public static class SvRoles
    {
        public const string Service = "SERVICE";
        public const string Ceo = "CEO";
        public const string Writers = "SERVICE";

        /// <summary>SALES reads customers and the units of its customers; the finance and quality roles read what concerns them.</summary>
        public const string Readers = "SERVICE,CEO,PMO,SALES,COMMERCIAL,FINANCEMANAGER,FINANCIAL,PROJECTMANAGER,QA,QC,AUDITOR";
        /// <summary>Who reads service orders: the readers plus the warehouse, which issues and delivers the parts of an order.</summary>
        public const string OrderReaders = Readers + ",WAREHOUSE,WHSUPERVISOR";

        public const string SettingSetters = "CEO";
        public const string GoodwillDeciders = "CEO";

        /// <summary>Who sees the labour cost of an order when it could show one person's hourly pay.</summary>
        public const string PayrollSeers = "PAYROLL,FINANCEMANAGER,CEO";
    }

    internal static class SvNumbers
    {
        public const int Unit = 511;
        public const int Request = 512;
        public const int Order = 513;
        public const int Contract = 514;
        public const int Survey = 515;
    }

    public static class SvSettingKeys
    {
        /// <summary>Code of the cost centre «پس از فروش» (created on first use when it does not exist). Default AFTERSALES.</summary>
        public const string CostCenterCode = "CostCenterCode";

        /// <summary>Code of the work centre whose standard labour rate is used for an engineer without an approved payslip. No default: finance sets it.</summary>
        public const string LabourWorkCenterCode = "LabourWorkCenterCode";

        /// <summary>Days after the handover when the delivery survey is drafted. Default 30 (the brief; an assumption the company can change).</summary>
        public const string SurveyLeadDays = "SurveyLeadDays";

        /// <summary>Days before the due date of an LTSA visit when its service order is drafted. Default 30 (the brief; an assumption the company can change).</summary>
        public const string VisitLeadDays = "VisitLeadDays";

        public const string DefaultCostCenter = "AFTERSALES";
        public const int DefaultSurveyLeadDays = 30;
        public const int DefaultVisitLeadDays = 30;

        public static readonly (string Key, string Title)[] All =
        {
            (CostCenterCode, "کد مرکز هزینه پس از فروش (پیش‌فرض AFTERSALES)"),
            (LabourWorkCenterCode, "کد مرکز کاری که نرخ ساعتی دستمزد استاندارد آن برای مهندسانِ بدون فیش حقوقی تأییدشده به کار می‌رود (پیش‌فرض ندارد)"),
            (SurveyLeadDays, "روز بعد از تحویل برای پیش‌نویس نظرسنجی (پیش‌فرض ۳۰)"),
            (VisitLeadDays, "روز قبل از موعد بازدید LTSA برای پیش‌نویس دستور خدمات (پیش‌فرض ۳۰)"),
        };
    }

    internal static class SvSettings
    {
        public static async Task<string> TextAsync(IUnitOfWork uow, string key, CancellationToken ct) =>
            SlText.Clean(await uow.Set<SvSetting>().AsNoTracking().Where(s => s.Key == key && !s.Deleted).Select(s => s.Text).FirstOrDefaultAsync(ct));

        public static async Task<int> IntAsync(IUnitOfWork uow, string key, int fallback, CancellationToken ct)
        {
            string text = await TextAsync(uow, key, ct);
            return text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= 0 ? v : fallback;
        }

        public static async Task<string> CostCenterAsync(IUnitOfWork uow, CancellationToken ct) =>
            await TextAsync(uow, SvSettingKeys.CostCenterCode, ct) ?? SvSettingKeys.DefaultCostCenter;

        /// <summary>The after-sales cost centre exists (created in the same save when it does not).</summary>
        public static async Task<string> EnsureCostCenterAsync(IUnitOfWork uow, CancellationToken ct)
        {
            string code = await CostCenterAsync(uow, ct);
            bool exists = await uow.Set<CostCenter>().AnyAsync(c => c.Code == code, ct);
            if (!exists && !uow.Set<CostCenter>().Local.Any(c => c.Code == code))
                uow.Set<CostCenter>().Add(new CostCenter { Code = code, Title = "پس از فروش", IsActive = true, IsProduction = false });
            return code;
        }
    }

    internal static class SvText
    {
        public static string Priority(SvPriority p) => p switch
        {
            SvPriority.Critical => "بحرانی",
            SvPriority.High => "بالا",
            SvPriority.Normal => "عادی",
            _ => "پایین",
        };

        public static string RequestType(SvRequestType t) => t switch
        {
            SvRequestType.Breakdown => "خرابی",
            SvRequestType.Inspection => "بازرسی",
            SvRequestType.Commissioning => "راه‌اندازی",
            SvRequestType.Overhaul => "تعمیرات اساسی",
            SvRequestType.SpareParts => "قطعات یدکی",
            SvRequestType.TechnicalQuery => "پرسش فنی",
            _ => "شکایت",
        };

        public static string RequestStatus(SvRequestStatus s) => s switch
        {
            SvRequestStatus.Open => "باز",
            SvRequestStatus.Assigned => "واگذارشده",
            SvRequestStatus.InProgress => "در حال انجام",
            SvRequestStatus.WaitingCustomer => "منتظر مشتری",
            SvRequestStatus.Resolved => "رفع‌شده",
            _ => "بسته‌شده",
        };

        public static string UnitStatus(SvUnitStatus s) => s switch
        {
            SvUnitStatus.InWarranty => "در گارانتی",
            SvUnitStatus.OutOfWarranty => "خارج از گارانتی",
            SvUnitStatus.UnderLtsa => "تحت قرارداد خدمات",
            _ => "از رده خارج",
        };

        public static string OrderStatus(SvOrderStatus s) => s switch
        {
            SvOrderStatus.Draft => "پیش‌نویس",
            SvOrderStatus.Planned => "برنامه‌ریزی‌شده",
            SvOrderStatus.InProgress => "در حال انجام",
            SvOrderStatus.Closed => "بسته‌شده",
            _ => "لغوشده",
        };

        public static string Decision(SvWarrantyDecision d) => d switch
        {
            SvWarrantyDecision.Warranty => "گارانتی",
            SvWarrantyDecision.Billable => "قابل‌فاکتور",
            SvWarrantyDecision.Goodwill => "حسن نیت",
            _ => "تصمیم‌گیری نشده",
        };

        public static string TimeKind(SvTimeKind k) => k switch
        {
            SvTimeKind.Travel => "سفر",
            SvTimeKind.Work => "کار",
            _ => "انتظار",
        };

        public static string ContractStatus(SvContractStatus s) => s switch
        {
            SvContractStatus.Draft => "پیش‌نویس",
            SvContractStatus.Active => "فعال",
            SvContractStatus.Expired => "منقضی",
            _ => "لغوشده",
        };

        /// <summary>Persian date and the clock time (HH:mm), for the due times of the SLA and the other instants where the hour matters.</summary>
        public static string Stamp(DateTime? d) => d.HasValue ? SlText.Date(d.Value) + " " + d.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : null;

        public static string Survey(SlSurveyKind k) => k == SlSurveyKind.Delivery ? "تحویل" : "خدمات";

        public static string SurveyStatus(SlSurveyStatus s) => s switch
        {
            SlSurveyStatus.Drafted => "پیش‌نویس",
            SlSurveyStatus.Sent => "ارسال‌شده",
            SlSurveyStatus.Answered => "پاسخ‌داده‌شده",
            _ => "ردشده",
        };
    }

    // ============================================================================================== pure rules (unit tested)

    internal static class SvSla
    {
        /// <summary>Received time + the SLA hours; empty when the priority has no SLA.</summary>
        public static DateTime? Due(DateTime received, decimal? hours) => hours.HasValue ? received.AddHours((double)hours.Value) : (DateTime?)null;

        /// <summary>Breached: it was done after the due time, or it is not done and the due time has passed on <paramref name="asOf"/>.</summary>
        public static bool Breached(DateTime? due, DateTime? doneAt, DateTime asOf) => due.HasValue && (doneAt ?? asOf) > due.Value;
    }

    internal static class SvWarranty
    {
        public static DateTime End(DateTime start, int months) => start.Date.AddMonths(months);

        /// <summary>Warranty covers the whole of the start and the end day.</summary>
        public static bool Covers(DateTime? start, DateTime? end, DateTime on) => start.HasValue && end.HasValue && on.Date >= start.Value.Date && on.Date <= end.Value.Date;

        /// <summary>The state of a unit on a day: decommissioned, under an active LTSA, in warranty, or out.</summary>
        public static SvUnitStatus Status(bool decommissioned, bool underLtsa, bool inWarranty) =>
            decommissioned ? SvUnitStatus.Decommissioned : inWarranty ? SvUnitStatus.InWarranty : underLtsa ? SvUnitStatus.UnderLtsa : SvUnitStatus.OutOfWarranty;

        /// <summary>The rule of a warranty decision. Returns a Persian message, or null when it is allowed.</summary>
        /// <param name="coveredOnTestDate">The warranty covers the day the failure was reported.</param>
        public static string CheckDecision(SvWarrantyDecision decision, bool coveredOnTestDate, bool misuse, bool requestedGoodwill)
        {
            switch (decision)
            {
                case SvWarrantyDecision.Warranty:
                    if (!coveredOnTestDate) return "واحد در تاریخ گزارش خرابی در گارانتی نبوده است؛ تصمیم «گارانتی» ممکن نیست.";
                    if (misuse) return "علت خرابی سوءاستفاده مشتری است؛ تصمیم «گارانتی» ممکن نیست.";
                    return null;
                case SvWarrantyDecision.Billable:
                    return null;
                case SvWarrantyDecision.Goodwill:
                    return requestedGoodwill ? null : "برای «حسن نیت» ابتدا باید درخواست حسن نیت با دلیل ثبت شود.";
                default:
                    return "تصمیم گارانتی نامعتبر است.";
            }
        }
    }

    internal static class SvCostMath
    {
        public static decimal Labour(decimal hours, decimal ratePerHour) => SlText.R0(hours * ratePerHour);

        /// <summary>Warranty and goodwill orders are not billed; a billable order is billed per work and travel hour (waiting is not billed).</summary>
        public static decimal BillableHours(decimal travel, decimal work) => travel + work;
    }

    internal static class SvLtsa
    {
        /// <summary>The dates of the periodic visits: start + k x interval months, for k = 1, 2, ... up to the end date.</summary>
        public static List<DateTime> VisitDates(DateTime start, DateTime end, int intervalMonths)
        {
            var dates = new List<DateTime>();
            if (intervalMonths <= 0) return dates;
            for (int k = 1; k < 1000; k++)
            {
                DateTime d = start.Date.AddMonths(k * intervalMonths);
                if (d > end.Date) break;
                dates.Add(d);
            }
            return dates;
        }

        /// <summary>A visit is drafted when the day is within the lead days of its date.</summary>
        public static bool Due(DateTime visit, DateTime asOf, int leadDays) => asOf.Date >= visit.Date.AddDays(-leadDays);
    }

    internal static class SvKpiMath
    {
        /// <summary>Average of the hours between two instants, two decimals; empty without a pair.</summary>
        public static decimal? AverageHours(IEnumerable<(DateTime From, DateTime To)> pairs)
        {
            List<double> hours = pairs.Select(p => (p.To - p.From).TotalHours).ToList();
            return hours.Count == 0 ? (decimal?)null : SlText.R2((decimal)hours.Average());
        }

        /// <summary>Mean time between failures: operating hours divided by the failures; empty without a failure.</summary>
        public static decimal? Mtbf(decimal operatingHours, int failures) => failures > 0 ? SlText.R2(operatingHours / failures) : (decimal?)null;

        /// <summary>Failures after the first one.</summary>
        public static int Repeats(int failures) => Math.Max(0, failures - 1);
    }

    internal static class SvSurveyMath
    {
        public static bool ValidScore(int score) => score >= 1 && score <= 5;

        /// <summary>Average of the given scores, two decimals; empty without a score.</summary>
        public static decimal? Average(IEnumerable<int> scores)
        {
            List<int> list = scores.ToList();
            return list.Count == 0 ? (decimal?)null : SlText.R2((decimal)list.Sum() / list.Count);
        }

        public static readonly string[] Questions =
        {
            "کیفیت محصول",
            "رعایت زمان تحویل یا زمان پاسخ‌گویی",
            "رفتار و تخصص کارکنان",
            "رضایت کلی از همکاری",
        };
    }

    internal static class SvPersianYear
    {
        /// <summary>The first day (1 Farvardin) of the Persian year of the given day.</summary>
        public static DateTime Start(DateTime day)
        {
            var calendar = new PersianCalendar();
            return calendar.ToDateTime(calendar.GetYear(day), 1, 1, 0, 0, 0, 0);
        }
    }

    /// <summary>Cost privacy: the labour of an order is one person's pay when fewer than three people are behind it.</summary>
    internal static class SvPrivacy
    {
        public static bool LabourProtected(int employees, int payrollEmployees) => Production.MfCostMask.IsProtected(employees, payrollEmployees);
    }

    /// <summary>The lengths of the columns of the service tables (the same figures as the model configuration): a longer text is refused with a message instead of failing in the database.</summary>
    internal static class SvLimits
    {
        public const int Code = 50;
        public const int Name = 200;
        public const int Title = 300;
        public const int Text = 1000;
        public const int LongText = 4000;

        private static string Digits(int n) => string.Concat(n.ToString(CultureInfo.InvariantCulture).Select(c => (char)('\u06f0' + (c - '0'))));

        /// <summary>A Persian message when the (cleaned) text is longer than the column, else null.</summary>
        public static string TooLong(string cleaned, int max, string label) =>
            cleaned != null && cleaned.Length > max ? $"{label} بیش از اندازه طولانی است؛ حداکثر {Digits(max)} نویسه." : null;

        /// <summary>The first message among several (text, column length, label) fields.</summary>
        public static string First(params (string Text, int Max, string Label)[] fields)
        {
            foreach (var f in fields)
            {
                string m = TooLong(SlText.Clean(f.Text), f.Max, f.Label);
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>The text cut to the column (a text built by the system from a validated one).</summary>
        public static string Cut(string text, int max) => text == null || text.Length <= max ? text : text.Substring(0, max);
    }

    /// <summary>Running the jobs by hand for a day in the future changes real data (visits drafted, agreements expired), so it needs the switch Service:AllowFutureAsOf.</summary>
    internal static class SvJobsGuard
    {
        public const string AllowFutureKey = "Service:AllowFutureAsOf";

        public static string Refusal(DateTime asOf, DateTime today, bool allowFuture) =>
            asOf.Date > today.Date && !allowFuture
                ? "اجرای دستی کارها برای روزی در آینده داده‌های واقعی را عوض می‌کند و مجاز نیست؛ فقط وقتی تنظیم Service:AllowFutureAsOf روشن باشد (آزمایش) مجاز است."
                : null;
    }

    /// <summary>A parts document of a service order against the state of the order.</summary>
    internal static class SvDocumentRules
    {
        /// <summary>A draft parts document is confirmed only while the order is planned or in progress.</summary>
        public static string CheckConfirm(SvOrderStatus status, string orderNumber) =>
            status == SvOrderStatus.Planned || status == SvOrderStatus.InProgress
                ? null
                : $"دستور خدمات {orderNumber} در وضعیت «{SvText.OrderStatus(status)}» است و سند قطعات آن تأیید نمی‌شود.";

        /// <summary>A confirmed parts document is cancelled only while its order is not closed (the cost is posted at the close).</summary>
        public static string CheckCancel(SvOrderStatus status, string orderNumber) =>
            status == SvOrderStatus.Closed
                ? $"دستور خدمات {orderNumber} بسته شده و هزینه‌ی قطعات آن ثبت شده است؛ سند قطعات آن باطل نمی‌شود."
                : null;
    }

    /// <summary>The accounting event of a warranty or goodwill order at its close. Labour absorbed goes to its own service account (70113, not the production labour), the parts left on site are cost of sales (as their delivery document posted) and the parts consumed are material expense.
    /// The labour passed here is zero for an order whose labour is protected: that labour is not posted per order but in the monthly aggregate (<see cref="SvLabourMonth"/>).</summary>
    internal static class SvPosting
    {
        public static List<(string Role, decimal Debit, decimal Credit, string Description)> WarrantyLines(bool warranty, decimal labour, decimal partsConsumed, decimal partsLeftOnSite, string number)
        {
            decimal total = labour + partsConsumed + partsLeftOnSite;
            var lines = new List<(string, decimal, decimal, string)>
            {
                (warranty ? AccountRoles.WarrantyProvision : AccountRoles.WarrantyExpense, total, 0m, "هزینه خدمات " + (warranty ? "گارانتی" : "حسن نیت") + " " + number),
            };
            if (labour != 0) lines.Add((AccountRoles.ServiceLabourAbsorbed, 0m, labour, "دستمزد جذب‌شده در خدمات " + number));
            if (partsConsumed != 0) lines.Add((AccountRoles.MaterialExpense, 0m, partsConsumed, "قطعات مصرفی خدمات " + number));
            if (partsLeftOnSite != 0) lines.Add((AccountRoles.Cogs, 0m, partsLeftOnSite, "قطعات باقی‌مانده در سایت خدمات " + number));
            return lines;
        }
    }

    /// <summary>One closed order whose protected labour waits for its monthly aggregate posting (the pure input of <see cref="SvLabourMonth"/>).</summary>
    internal sealed class SvPendingLabour
    {
        public int OrderId { get; set; }
        public SvWarrantyDecision Decision { get; set; }
        public int? ContractId { get; set; }
        public string CostCenter { get; set; }
        public decimal Labour { get; set; }

        /// <summary>The employees with hours on the order, and those among them paid through an approved payslip (their hourly pay is the personal data).</summary>
        public HashSet<int> Employees { get; set; } = new HashSet<int>();
        public HashSet<int> PayrollEmployees { get; set; } = new HashSet<int>();
    }

    /// <summary>
    /// Monthly aggregate posting of protected labour (the labour of an order that fewer than three payroll-rated employees worked on; binding rule: payroll personal data only PAYROLL / FINANCEMANAGER / CEO).
    /// A per-order posting of such labour would let every reader of the accounting events (PMO, CEO, FINANCIAL, FINANCEMANAGER, PLANNING) and of the cost entries divide the amount by the visible hours of the order and get one person's hourly pay.
    /// So at the close the labour of a protected order is only marked pending for a Persian month; the parts part (not personal) is still posted at once. The monthly run posts ONE accounting event per month with the summed labour of all the pending orders
    /// of the month (no order number, no per-order line) and one labour cost entry per contract and month.
    /// The month of an order is the month it closes in; when that month already has its aggregate posting (only possible with an injected future day) the order goes to the first later month that has none: a posted month is never touched again.
    /// </summary>
    internal static class SvLabourMonth
    {
        public const string SourceType = "ServiceLabourMonth";

        private static readonly PersianCalendar Calendar = new PersianCalendar();

        /// <summary>The Persian month of a day as yyyymm (for example 140507).</summary>
        public static int Of(DateTime day) => Calendar.GetYear(day) * 100 + Calendar.GetMonth(day);

        public static int Next(int period) => period % 100 == 12 ? (period / 100 + 1) * 100 + 1 : period + 1;

        /// <summary>The last day of the Persian month; the date of its aggregate posting.</summary>
        public static DateTime End(int period)
        {
            int year = period / 100, month = period % 100;
            return Calendar.ToDateTime(year, month, Calendar.GetDaysInMonth(year, month), 0, 0, 0, 0);
        }

        public static string Text(int period) => $"{period / 100}/{(period % 100).ToString("00", CultureInfo.InvariantCulture)}";
        public static string Number(int period) => "SVL-" + period.ToString(CultureInfo.InvariantCulture);

        /// <summary>The labour of the order is not posted at its close: it is protected and not zero.</summary>
        public static bool Deferred(decimal labour, bool labourProtected) => labourProtected && labour != 0;

        /// <summary>The month an order closed on <paramref name="closeDay"/> is aggregated in: the month of the close, or the first later month without a posting.</summary>
        public static int Assign(DateTime closeDay, IEnumerable<int> postedPeriods)
        {
            HashSet<int> posted = postedPeriods.ToHashSet();
            int period = Of(closeDay);
            while (posted.Contains(period)) period = Next(period);
            return period;
        }

        /// <summary>A month is posted by a run on <paramref name="asOf"/> only when it has ended (the run day is in a later month).</summary>
        public static bool Due(int period, DateTime asOf) => period < Of(asOf);

        private static bool Protected(IEnumerable<SvPendingLabour> orders)
        {
            List<SvPendingLabour> list = orders.ToList();
            return SvPrivacy.LabourProtected(list.SelectMany(o => o.Employees).Distinct().Count(), list.SelectMany(o => o.PayrollEmployees).Distinct().Count());
        }

        /// <summary>
        /// The lines of the one accounting event of a month (warranty and goodwill orders only; a billable order and the visit of an agreement are paid by the customer and post no event). Debit: the warranty provision for the warranty labour and the
        /// goodwill expense for the goodwill labour; credit: the service labour absorbed. Never an order number, never a per-order line. When both kinds exist but one of them comes from fewer than three payroll-rated employees, that split would show
        /// a person's pay again, so both go to one debit line of the larger kind (warranty on a tie): the books get the exact month total, the split is not published.
        /// </summary>
        public static List<(string Role, decimal Debit, decimal Credit, string Description)> EventLines(IEnumerable<SvPendingLabour> orders, int period)
        {
            List<SvPendingLabour> warranty = orders.Where(o => o.Decision == SvWarrantyDecision.Warranty).ToList();
            List<SvPendingLabour> goodwill = orders.Where(o => o.Decision == SvWarrantyDecision.Goodwill).ToList();
            decimal w = warranty.Sum(o => o.Labour), g = goodwill.Sum(o => o.Labour);
            var lines = new List<(string, decimal, decimal, string)>();
            string label = "ماه " + Text(period);
            if (w + g == 0) return lines;
            bool split = w != 0 && g != 0 && !Protected(warranty) && !Protected(goodwill);
            if (split)
            {
                lines.Add((AccountRoles.WarrantyProvision, w, 0m, "هزینه خدمات گارانتی " + label));
                lines.Add((AccountRoles.WarrantyExpense, g, 0m, "هزینه خدمات حسن نیت " + label));
            }
            else if (w != 0 && g != 0)
                lines.Add((w >= g ? AccountRoles.WarrantyProvision : AccountRoles.WarrantyExpense, w + g, 0m, "هزینه خدمات پس از فروش " + label));
            else
                lines.Add((w != 0 ? AccountRoles.WarrantyProvision : AccountRoles.WarrantyExpense, w + g, 0m, "هزینه خدمات " + (w != 0 ? "گارانتی" : "حسن نیت") + " " + label));
            lines.Add((AccountRoles.ServiceLabourAbsorbed, 0m, w + g, "دستمزد جذب‌شده در خدمات پس از فروش " + label));
            return lines;
        }

        /// <summary>
        /// The labour cost entries of a month (every kind of order): one per contract and cost centre. A contract whose own share comes from fewer than three payroll-rated employees would show a person's pay in the contract cost list, so its share
        /// joins the entry without a contract of the same cost centre (the cost stays in the cost centre and the books; only the contract allocation of that small share is lost).
        /// </summary>
        public static List<(int? ContractId, string CostCenter, decimal Amount)> CostGroups(IEnumerable<SvPendingLabour> orders)
        {
            var result = new Dictionary<(int?, string), decimal>();
            foreach (var group in orders.Where(o => o.Labour != 0).GroupBy(o => (o.ContractId, o.CostCenter)))
            {
                (int?, string) key = group.Key.ContractId.HasValue && !Protected(group) ? (group.Key.ContractId, group.Key.CostCenter) : ((int?)null, group.Key.CostCenter);
                result[key] = result.GetValueOrDefault(key) + group.Sum(o => o.Labour);
            }
            return result.OrderBy(x => x.Key.Item1 ?? int.MaxValue).ThenBy(x => x.Key.Item2, StringComparer.Ordinal).Select(x => (x.Key.Item1, x.Key.Item2, x.Value)).ToList();
        }
    }

    /// <summary>The price of a spare part on the invoice of a billable order: the approved price list valid on the day (never an invented price).</summary>
    internal static class SvPricing
    {
        /// <summary>The newest list (valid from, then id) that has a line for the item with a minimum quantity not above the quantity; within a list the highest minimum quantity wins.</summary>
        public static decimal? PriceOf(IEnumerable<SlPriceList> lists, int itemId, decimal quantity) =>
            lists.OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.Id)
                .SelectMany(p => p.Lines.Where(x => x.ItemId == itemId && x.MinQuantity <= quantity).OrderByDescending(x => x.MinQuantity).Take(1))
                .Select(x => (decimal?)x.UnitPrice).FirstOrDefault();
    }
}
