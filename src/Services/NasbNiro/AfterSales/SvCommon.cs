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

        /// <summary>R1p D25: only QA verifies a sample of the survey answers that SERVICE or SALES typed in (the people the scores are about must not confirm them themselves).</summary>
        public const string SurveyVerifiers = "QA";

        /// <summary>The roles that read payroll-derived figures (a protected quarter-end labour amount, the names of the pool).</summary>
        public const string PayrollSeers = "PAYROLL,FINANCEMANAGER,CEO";

        /// <summary>R1p D2: who sees the engineers named on an order and on its time entries: SERVICE and the payroll readers (the engineer himself sees his own entries as well). Everybody else sees hours without a name.</summary>
        public const string NameSeers = Service + "," + PayrollSeers;

        /// <summary>
        /// Whether the caller sees the labour cost. A seat or the evaluation user (it acts as CEO, but its text is read by other people) never does: it gets the masked form.
        /// </summary>
        public static bool SeesPayroll(bool inRole) => inRole && !Ai.AiActing.IsActing;

        /// <summary>The same rule for the names of the engineers: a seat of the AI never sees them.</summary>
        public static bool SeesNames(bool inRole) => inRole && !Ai.AiActing.IsActing;
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

        /// <summary>
        /// Code of the work centre whose STANDARD hourly labour rate (<see cref="Entities.Model.NasbNiro.Production.MfWorkCenter.LabourRateRial"/>, maintained per Persian year by finance) the labour of every closed service order is absorbed
        /// at, whoever worked it. No default: finance sets it; without it (or with a centre that has no rate and no Production:StandardLabourRate) closing an order that has hours is refused and names the centre.
        /// </summary>
        public const string LabourWorkCenterCode = "LabourWorkCenterCode";

        /// <summary>Days after the handover when the delivery survey is drafted. Default 30 (the brief; an assumption the company can change).</summary>
        public const string SurveyLeadDays = "SurveyLeadDays";

        /// <summary>Days before the due date of an LTSA visit when its service order is drafted. Default 30 (the brief; an assumption the company can change).</summary>
        public const string VisitLeadDays = "VisitLeadDays";

        /// <summary>
        /// R1p D25: a customer survey score below this opens a customer complaint draft. The configuration key Service:LowScoreThreshold is the fallback when the CEO has not entered a value here; default 3 (★ provisional decision).
        /// </summary>
        public const string LowScoreThreshold = "LowScoreThreshold";

        public const string DefaultCostCenter = "AFTERSALES";
        public const int DefaultSurveyLeadDays = 30;
        public const int DefaultVisitLeadDays = 30;
        public const int DefaultLowScoreThreshold = 3;

        public static readonly (string Key, string Title)[] All =
        {
            (CostCenterCode, "کد مرکز هزینه پس از فروش (پیش‌فرض AFTERSALES)"),
            (LabourWorkCenterCode, "کد مرکز کاری که نرخ ساعتی دستمزد استاندارد آن برای جذب دستمزد همه‌ی دستورهای خدمات به کار می‌رود (پیش‌فرض ندارد)"),
            (SurveyLeadDays, "روز بعد از تحویل برای پیش‌نویس نظرسنجی (پیش‌فرض ۳۰)"),
            (VisitLeadDays, "روز قبل از موعد بازدید LTSA برای پیش‌نویس دستور خدمات (پیش‌فرض ۳۰)"),
            (LowScoreThreshold, "امتیاز آستانه‌ی نظرسنجی: امتیاز کمتر از این مقدار (۱ تا ۵) پیش‌نویس شکایت مشتری باز می‌کند (پیش‌فرض ۳)"),
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

    /// <summary>
    /// R1p D24: the clocks of the SLA are the server's. An action (first response, resolution, assignment) is stamped with the server time; the client never sends it. The received time of a request is the server time too,
    /// except that a person registering a phone call or a letter may enter the time it really came in, only within the last 24 hours (★ provisional decision) and not in the future: a request that came in earlier than that is
    /// registered now and its clock starts now. A channel that is the system itself (e-mail, portal, anything else) always gets the server time. The time of a request is never changed after it is registered.
    /// </summary>
    internal static class SvSlaTime
    {
        /// <summary>How far back a person may date a registered call or letter.</summary>
        public const int BackdateHours = 24;

        /// <summary>Clock difference between the client and the server that is not held against the client.</summary>
        public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

        public const string ActionRefusal = "زمان اقدام از ساعت سرور ثبت می‌شود و قابل تعیین نیست.";
        public const string ChannelRefusal = "زمان دریافت را فقط برای ثبت تماس تلفنی یا نامه می‌توان وارد کرد؛ برای کانال‌های دیگر (ایمیل، سامانه و غیره) زمان دریافت همان زمان ثبت در سرور است.";
        public const string FutureRefusal = "زمان دریافت در آینده است.";
        public const string TooOldRefusal = "زمان دریافت بیش از ۲۴ ساعت قبل است؛ درخواست را بدون زمان دریافت ثبت کنید تا ساعت SLA از زمان ثبت شروع شود.";
        public const string ChangeRefusal = "زمان دریافت پس از ثبت درخواست تغییر نمی‌کند.";

        private static readonly string[] CallOrLetterWords = { "تلفن", "تماس", "نامه", "فکس", "phone", "call", "letter", "fax" };

        private static string Normal(string text) => (text ?? string.Empty).Replace('ي', 'ی').Replace('ك', 'ک').Trim().ToLowerInvariant();

        /// <summary>A phone call or a letter: the channels in which a person types in what came from outside at some earlier moment.</summary>
        public static bool IsCallOrLetter(string channel)
        {
            string c = Normal(channel);
            return c.Length > 0 && CallOrLetterWords.Any(w => c.Contains(w));
        }

        /// <summary>The Persian refusal of a client-supplied received time of a NEW request, or null when it is allowed (or not supplied).</summary>
        public static string CheckReceived(DateTime? supplied, string channel, DateTime now)
        {
            if (!supplied.HasValue) return null;
            if (!IsCallOrLetter(channel)) return ChannelRefusal;
            if (supplied.Value > now + FutureTolerance) return FutureRefusal;
            if (supplied.Value < now.AddHours(-BackdateHours)) return TooOldRefusal;
            return null;
        }

        /// <summary>The received time of a new request: the one supplied (a call or a letter, already checked, never later than now) or the server time.</summary>
        public static DateTime ReceivedOf(DateTime? supplied, DateTime now) => supplied.HasValue ? (supplied.Value > now ? now : supplied.Value) : now;

        /// <summary>An edit may send the stored received time back, but not another one.</summary>
        public static string CheckUnchanged(DateTime? supplied, DateTime stored) =>
            supplied.HasValue && Math.Abs((supplied.Value - stored).TotalSeconds) > 1 ? ChangeRefusal : null;

        /// <summary>An action carries no client time.</summary>
        public static string CheckAction(DateTime? supplied) => supplied.HasValue ? ActionRefusal : null;
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
        /// <param name="datesConfirmed">
        /// R1p D26: the dates of the unit (handover, commissioning, warranty start and end) were confirmed by a second person. A decision that grants the warranty rests on those dates, so it is refused while they are
        /// the unchecked entry of one person (a unit edited after its confirmation goes back to unconfirmed). Billable and goodwill decisions do not depend on the dates.
        /// </param>
        public static string CheckDecision(SvWarrantyDecision decision, bool coveredOnTestDate, bool misuse, bool requestedGoodwill, bool datesConfirmed = true)
        {
            switch (decision)
            {
                case SvWarrantyDecision.Warranty:
                    if (!datesConfirmed) return "تاریخ‌های گارانتی این واحد هنوز توسط شخص دوم تأیید نشده است؛ تصمیم «گارانتی» ممکن نیست.";
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

        /// <summary>
        /// R1p D36: the meter hours a unit ran INSIDE the period (the failures of the period are divided by the hours of the same period, not by the whole life of the unit).
        /// The hours are the meter reading at the end of the period less the reading at its start. The readings are the ones of the closed service orders. The reading at the start is the last one before the period; a unit commissioned
        /// inside the period started at zero; an open start (no from-date) counts from zero. Empty (the unit is left out of the hours AND of the failures) when the period cannot be measured for the unit: no reading before the period
        /// for a unit older than the period, or no reading at all inside the period.
        /// </summary>
        public static decimal? PeriodHours(IEnumerable<(DateTime Date, decimal Hours)> readings, DateTime? from, DateTime? to, DateTime? commissioned)
        {
            List<(DateTime Date, decimal Hours)> list = readings.OrderBy(r => r.Date).ThenBy(r => r.Hours).ToList();
            DateTime low = from.HasValue ? from.Value.Date : DateTime.MinValue, high = to.HasValue ? to.Value.Date : DateTime.MaxValue;
            if (!list.Any(r => r.Date >= low && r.Date <= high)) return null;
            decimal end = list.Last(r => r.Date <= high).Hours;
            decimal start;
            if (!from.HasValue) start = 0;
            else if (list.Any(r => r.Date < low)) start = list.Last(r => r.Date < low).Hours;
            else if (commissioned.HasValue && commissioned.Value.Date >= low) start = 0;
            else return null;
            return Math.Max(0, end - start);
        }

        /// <summary>Failures after the first one.</summary>
        public static int Repeats(int failures) => Math.Max(0, failures - 1);
    }

    internal static class SvSurveyMath
    {
        public static bool ValidScore(int score) => score >= 1 && score <= 5;

        public const string ConfigKey = "Service:LowScoreThreshold";

        /// <summary>
        /// R1p D25: the threshold below which a score opens a complaint draft. The first usable of the CEO's setting, the configuration value and the default (3, ★ provisional); kept between 1 and 5 (a score is 1 to 5, so 1 never complains and 5 complains for 1 to 4).
        /// </summary>
        public static int Threshold(string setting, string configured)
        {
            foreach (string text in new[] { setting, configured })
                if (text != null && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= 1) return Math.Min(v, 5);
            return SvSettingKeys.DefaultLowScoreThreshold;
        }

        /// <summary>The questions scored below the threshold.</summary>
        public static List<(int RowNumber, string Question, int Score)> LowScores(IEnumerable<(int RowNumber, string Question, int? Score)> answers, int threshold) =>
            answers.Where(a => a.Score.HasValue && a.Score.Value < threshold).OrderBy(a => a.RowNumber).Select(a => (a.RowNumber, a.Question, a.Score.Value)).ToList();

        /// <summary>A survey is answered only when every question has a score.</summary>
        public static bool AllAnswered(IEnumerable<int?> scores)
        {
            List<int?> list = scores.ToList();
            return list.Count > 0 && list.All(s => s.HasValue);
        }

        /// <summary>R1p D25: the surveys whose average is a figure: answered, with a score, and verified by QA with the customer. Every satisfaction average (service dashboard, CEO dashboard, audit review) reads this set.</summary>
        public static IQueryable<SlSurvey> VerifiedAnswers(IQueryable<SlSurvey> surveys) =>
            surveys.Where(s => !s.Deleted && s.Status == SlSurveyStatus.Answered && s.Verified && s.AverageScore != null);

        /// <summary>The answered surveys nobody has verified yet: counted next to the average, never part of it.</summary>
        public static IQueryable<SlSurvey> UnverifiedAnswers(IQueryable<SlSurvey> surveys) =>
            surveys.Where(s => !s.Deleted && s.Status == SlSurveyStatus.Answered && !s.Verified);

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
    /// The labour passed here is absorbed at the STANDARD rate of the after-sales labour work centre (R1p D2): the difference to the real payroll cost is posted once a month at company level (<see cref="SvLabourService"/>).</summary>
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

    /// <summary>
    /// Month arithmetic and names of the service labour months (R1p D2; the rule itself is described at the top of <see cref="SvLabourService"/>). Labour of a closed order is absorbed at the standard rate; an order with
    /// payroll-rated engineers is marked with the Persian month (yyyymm) whose variance includes it: the month of the close, or the first later month that is not closed yet (a closed month is never reopened).
    /// </summary>
    internal static class SvLabourMonth
    {
        public const string SourceType = "ServiceLabourMonth";

        private static readonly PersianCalendar Calendar = new PersianCalendar();

        /// <summary>The Persian month of a day as yyyymm (for example 140507).</summary>
        public static int Of(DateTime day) => Calendar.GetYear(day) * 100 + Calendar.GetMonth(day);

        public static int Next(int period) => period % 100 == 12 ? (period / 100 + 1) * 100 + 1 : period + 1;

        /// <summary>The last day of the Persian month; the date of its true-up posting.</summary>
        public static DateTime End(int period)
        {
            int year = period / 100, month = period % 100;
            return Calendar.ToDateTime(year, month, Calendar.GetDaysInMonth(year, month), 0, 0, 0, 0);
        }

        public static string Text(int period) => $"{period / 100}/{(period % 100).ToString("00", CultureInfo.InvariantCulture)}";
        public static string Number(int period) => "SVL-" + period.ToString(CultureInfo.InvariantCulture);

        /// <summary>The month an order closed on <paramref name="closeDay"/> is trued up in: the month of the close, or the first later month that is not closed yet.</summary>
        public static int Assign(DateTime closeDay, IEnumerable<int> closedPeriods)
        {
            HashSet<int> closed = closedPeriods.ToHashSet();
            int period = Of(closeDay);
            while (closed.Contains(period)) period = Next(period);
            return period;
        }

        /// <summary>A month is closed by a run on <paramref name="asOf"/> only when it has ended (the run day is in a later month).</summary>
        public static bool Due(int period, DateTime asOf) => period < Of(asOf);

        /// <summary>The month a carried pool waits in: the next one, or the first later one that is not closed (a closed month is never reopened).</summary>
        public static int NextOpen(int period, IEnumerable<int> closed)
        {
            HashSet<int> set = closed.ToHashSet();
            int next = Next(period);
            while (set.Contains(next)) next = Next(next);
            return next;
        }
    }

    /// <summary>
    /// R1p H3 (round 3): the read-side mask for service orders closed by the EARLIER release, which absorbed the labour at the engineers' own hourly pay. When only one or two of the engineers were paid through payroll
    /// the order's labour (and its total cost) is one person's pay and is returned as hidden to every reader but PAYROLL, FINANCEMANAGER and CEO. The marker only an old row has: nothing was absorbed through the pool
    /// (<c>LabourPooledAmount</c> empty or not positive: an order of this release always holds the positive amount its payroll-rated hours absorbed at the standard rate) AND the earlier release's group rule
    /// (<see cref="Production.MfLegacyLabour.Group"/>) holds for its engineer counts. A row converted by the backfill keeps a pooled amount of zero or less, so it stays masked. The CEO overview and drill use the same helper.
    /// </summary>
    internal static class SvLegacyLabour
    {
        public static bool IsProtected(decimal? pooledAmount, int employees, int payrollEmployees) => (pooledAmount ?? 0m) <= 0m && Production.MfLegacyLabour.Group(employees, payrollEmployees);

        public static bool IsProtected(SvServiceOrder order) => order != null && IsProtected(order.LabourPooledAmount, order.LabourEmployeeCount, order.LabourPayrollEmployeeCount);

        /// <summary>The same rule as a query condition (the dashboards and the CEO overview filter the closed orders with it).</summary>
        public static readonly System.Linq.Expressions.Expression<Func<SvServiceOrder, bool>> Filter =
            o => (o.LabourPooledAmount ?? 0m) <= 0m && o.LabourPayrollEmployeeCount > 0 && (o.LabourEmployeeCount < Production.MfLegacyLabour.MinEmployees || o.LabourPayrollEmployeeCount < Production.MfLegacyLabour.MinEmployees);

        /// <summary>The amount for a reader: empty (zero in the order DTO) when the order is protected and the reader is not a payroll reader.</summary>
        public static decimal Visible(decimal amount, bool isProtected, bool seesPayroll) => isProtected && !seesPayroll ? 0m : amount;
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
