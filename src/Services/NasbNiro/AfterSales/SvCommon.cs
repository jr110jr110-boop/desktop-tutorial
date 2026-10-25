using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLayer.Context;
using Entities.Model.NasbNiro.AfterSales;
using Entities.Model.NasbNiro.Finance;
using Microsoft.EntityFrameworkCore;
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
}
