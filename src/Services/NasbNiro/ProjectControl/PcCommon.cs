using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model;
using Entities.Model.NasbNiro.ProjectControl;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.ProjectControl
{
    /// <summary>Role names (as written in the JWT, upper case) for the project control module.</summary>
    public static class ProjectControlRoles
    {
        /// <summary>Planning manager: schedule, WBS, progress approval. The PMO deputy can do the same.</summary>
        public const string Planning = "PLANNING,PMO";
        public const string PlanningOnly = "PLANNING";
        public const string ProjectManagers = "PROJECTMANAGER,PMO";
        public const string PlanningOrPm = "PLANNING,PROJECTMANAGER,PMO";
        public const string Commercial = "COMMERCIAL,PMO";
        public const string Ceo = "CEO";
        public const string Pmo = "PMO";

        /// <summary>Project documents control and engineering move the document states.</summary>
        public const string Dcc = "DCC,ENGINEERING,ENGINEER,PMO";
        public const string DccOnly = "DCC,PMO";

        /// <summary>Unit managers propose manual progress and confirm daily reports.</summary>
        public const string UnitManagers = "ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER,QC,QA,COMMERCIAL,WAREHOUSE,WHSUPERVISOR,PLANNING,PROJECTMANAGER";

        public const string Quality = "QC";
        public const string Readers = "PMO,CEO,PROJECTMANAGER,PLANNING,DCC,COMMERCIAL,FINANCIAL,ENGINEERING,ENGINEER,QC,QA,CONSTRUCTION,ASSEMBLER,WAREHOUSE,WHSUPERVISOR";
    }

    /// <summary>Counter kinds in <c>DocumentNumberSequence</c> (project control uses 421..440).</summary>
    internal static class PcNumbers
    {
        public const int ContractYear = 421;
        public const int CategoryMa = 422;
        public const int CategorySp = 423;
        public const int CategorySe = 424;
        public const int ChangeRequest = 425;
        public const int Variation = 426;
        public const int Claim = 427;
        public const int Transmittal = 428;
        public const int Shipment = 429;
        public const int Report = 430;
        public const int Amendment = 431;
        public const int BookCode = 432;

        public static int CategoryKind(PcCategory c) => c == PcCategory.Manufacturing ? CategoryMa : c == PcCategory.SpareParts ? CategorySp : CategorySe;
        public static string CategoryLetters(PcCategory c) => c == PcCategory.Manufacturing ? "MA" : c == PcCategory.SpareParts ? "SP" : "SE";
    }

    /// <summary>Numbers the company can change. Defaults come from the procedures and the architect's provisional decisions (★).</summary>
    public static class PcSettingKeys
    {
        public const string AmberThreshold = "AmberThreshold";
        public const string RedThreshold = "RedThreshold";
        public const string ActionThreshold = "CorrectiveActionThreshold";
        public const string GuaranteeAlert1 = "GuaranteeAlertDays1";
        public const string GuaranteeAlert2 = "GuaranteeAlertDays2";
        public const string GuaranteeAlert3 = "GuaranteeAlertDays3";
        public const string EngFirstIssue = "EngineeringFirstIssuePercent";
        public const string EngSecondIssue = "EngineeringSecondIssuePercent";
        public const string EngApproved = "EngineeringApprovedPercent";
        public const string SupplyTemporary = "SupplyTemporaryReceiptPercent";
        public const string SupplyAccepted = "SupplyAcceptedPercent";
        public const string SupplyRework = "SupplyReworkPercent";
        public const string SupplyReject = "SupplyRejectPercent";
        public const string LongLeadDays = "LongLeadDays";
        public const string SnapshotHour = "SnapshotHour";
        public const string ChangeAnalysisDays = "ChangeAnalysisWorkingDays";
        public const string ReminderDays = "DueReminderDays";
        public const string CriticalFloat = "CriticalFloatDays";
        public const string RescheduleDelayPercent = "RescheduleDelayPercent";
        public const string SlaManager = "SlaManagerSelectedDays";
        public const string SlaPep = "SlaPepApprovedDays";
        public const string SlaBom = "SlaBomRequestedDays";
        public const string NoticeLookAheadDays = "NoticeLookAheadDays";

        public static IEnumerable<PcSetting> Defaults()
        {
            yield return new PcSetting { Key = AmberThreshold, Value = 0.95m, Title = "آستانه هشدار زرد برای SPI و CPI", Source = "★ تصمیم موقت" };
            yield return new PcSetting { Key = RedThreshold, Value = 0.90m, Title = "آستانه هشدار قرمز برای SPI و CPI", Source = "★ تصمیم موقت" };
            yield return new PcSetting { Key = ActionThreshold, Value = 1.00m, Title = "کمتر از این مقدار اقدام اصلاحی لازم است", Source = "WI-PL-009 بند 29" };
            yield return new PcSetting { Key = GuaranteeAlert1, Value = 30, Title = "هشدار انقضای ضمانت‌نامه (روز) - اول", Source = "★ تصمیم موقت" };
            yield return new PcSetting { Key = GuaranteeAlert2, Value = 14, Title = "هشدار انقضای ضمانت‌نامه (روز) - دوم", Source = "★ تصمیم موقت" };
            yield return new PcSetting { Key = GuaranteeAlert3, Value = 7, Title = "هشدار انقضای ضمانت‌نامه (روز) - سوم", Source = "★ تصمیم موقت" };
            yield return new PcSetting { Key = EngFirstIssue, Value = 60, Title = "پیشرفت مهندسی: ارسال اول (درصد)", Source = "WI-PL-009 بند 19" };
            yield return new PcSetting { Key = EngSecondIssue, Value = 80, Title = "پیشرفت مهندسی: ارسال دوم (درصد)", Source = "WI-PL-009 بند 19" };
            yield return new PcSetting { Key = EngApproved, Value = 100, Title = "پیشرفت مهندسی: تایید (درصد)", Source = "WI-PL-009 بند 19" };
            yield return new PcSetting { Key = SupplyTemporary, Value = 70, Title = "پیشرفت تامین: رسید موقت انبار (درصد)", Source = "WI-PL-009 بند 19" };
            yield return new PcSetting { Key = SupplyAccepted, Value = 100, Title = "پیشرفت تامین: تایید QC و رسید دائم (درصد)", Source = "WI-PL-009 بند 19" };
            yield return new PcSetting { Key = SupplyRework, Value = 80, Title = "پیشرفت تامین: بازکاری تا تایید (درصد)", Source = "WI-PL-009 بند 27" };
            yield return new PcSetting { Key = SupplyReject, Value = 0, Title = "پیشرفت تامین: مردود (درصد)", Source = "WI-PL-009 بند 27" };
            yield return new PcSetting { Key = LongLeadDays, Value = 60, Title = "حداقل زمان تامین اقلام زمان‌بر (روز)", Source = "★ تصمیم موقت" };
            yield return new PcSetting { Key = SnapshotHour, Value = 18, Title = "ساعت برداشت خودکار عکس هفتگی (ساعت سرور)", Source = "★ تصمیم موقت" };
            yield return new PcSetting { Key = ChangeAnalysisDays, Value = 2, Title = "مهلت تحلیل درخواست تغییر (روز کاری)", Source = "WI-PL-013" };
            yield return new PcSetting { Key = ReminderDays, Value = 3, Title = "یادآوری فعالیت‌های سررسید (روز قبل)", Source = "WI-PL-009 بند 21" };
            yield return new PcSetting { Key = CriticalFloat, Value = 0, Title = "حداکثر شناوری فعالیت بحرانی (روز کاری)", Source = "★ تصمیم موقت" };
            yield return new PcSetting { Key = RescheduleDelayPercent, Value = 10, Title = "تاخیر بالاتر از این درصد نیاز به زمان‌بندی مجدد دارد", Source = "WI-PL-009 بند 29" };
            yield return new PcSetting { Key = SlaManager, Value = 2, Title = "مهلت انتخاب مدیر پروژه (روز کاری)", Source = "WI-PL-009 بند 3" };
            yield return new PcSetting { Key = SlaPep, Value = 7, Title = "مهلت تایید برنامه اجرایی پروژه PEP (روز کاری)", Source = "WI-PL-009 بند 3-1" };
            yield return new PcSetting { Key = SlaBom, Value = 3, Title = "مهلت درخواست BOM (روز کاری)", Source = "WI-PL-009 بند 5" };
            yield return new PcSetting { Key = NoticeLookAheadDays, Value = 14, Title = "پیش‌نویس اعلام نقطه بازرسی تا چند روز آینده", Source = "★ تصمیم موقت" };
        }

        public static async Task<Dictionary<string, decimal>> LoadAsync(IUnitOfWork uow, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> result = Defaults().ToDictionary(d => d.Key, d => d.Value);
            List<PcSetting> rows = await uow.Set<PcSetting>().AsNoTracking().ToListAsync(cancellationToken);
            foreach (PcSetting row in rows) result[row.Key] = row.Value;
            return result;
        }
    }

    /// <summary>A page whose items were converted after the database paging.</summary>
    internal sealed class PcPage<T> : ViewModels.NasbNiro.Pagination.PagedList<T>
    {
        public PcPage(IEnumerable<T> items, int count, int pageNumber, int pageSize) : base(items, count, pageNumber, pageSize) { }
    }

    internal static class PcText
    {
        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        public static ResponseDto<T> NotFound<T>(string message) => ResponseDtoHelper.NotFound<T>(message);

        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string Concurrency = "اطلاعات هم‌زمان توسط کاربر دیگری تغییر کرد. صفحه را دوباره باز کنید و دوباره تلاش کنید.";
        public const string SameUser = "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف).";
        public const string ContractNotFound = "قرارداد پیدا نشد.";
        public const string WorkOrderNotFound = "دستور کار پیدا نشد.";

        public static string Upper(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

        public static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                return string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        public static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

        /// <summary>Legacy contract dates are non-nullable; 0001 means "unknown".</summary>
        public static DateTime? RealDate(DateTimeOffset value) => value.Year <= 1 ? (DateTime?)null : value.Date;

        public static int PersianYear(DateTime date) => new PersianCalendar().GetYear(date);
        public static int PersianMonth(DateTime date) => new PersianCalendar().GetMonth(date);

        /// <summary>The latest date on or before asOf that falls on the given weekday number.</summary>
        public static DateTime LastWeekday(DateTime asOf, int weekday)
        {
            DateTime d = asOf.Date;
            int diff = ((int)d.DayOfWeek - weekday + 7) % 7;
            return d.AddDays(-diff);
        }
    }

    /// <summary>Loads calendars and contract terms; shared by the services.</summary>
    internal static class PcContext
    {
        public static async Task<PcContractTerms> TermsAsync(IUnitOfWork uow, int contractId, bool track, CancellationToken cancellationToken)
        {
            IQueryable<PcContractTerms> q = uow.Set<PcContractTerms>().Where(t => t.ContractId == contractId && !t.Deleted);
            if (!track) q = q.AsNoTracking();
            return await q.FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>The working calendar of a contract: its own calendar, else the company calendar (weekly off-days and holidays).</summary>
        public static async Task<PcWorkCalendar> CalendarAsync(IUnitOfWork uow, int? calendarId, CancellationToken cancellationToken)
        {
            List<DateTime> holidays = (await uow.Set<HolidaysByDate>().AsNoTracking().Select(h => h.Date).ToListAsync(cancellationToken)).Select(d => d.Date).ToList();
            if (calendarId.HasValue)
            {
                PcCalendar cal = await uow.Set<PcCalendar>().AsNoTracking().Include(c => c.Exceptions).FirstOrDefaultAsync(c => c.Id == calendarId.Value && !c.Deleted, cancellationToken);
                if (cal != null)
                {
                    var off = new List<DateTime>(cal.Exceptions.Where(e => !e.IsWorking).Select(e => e.Date));
                    if (cal.UseCompanyHolidays) off.AddRange(holidays);
                    return new PcWorkCalendar(cal.WorkingWeekdays, off, cal.Exceptions.Where(e => e.IsWorking).Select(e => e.Date));
                }
            }
            List<DayOfWeek> weekly = await uow.Set<HolidaysDayOfWeek>().AsNoTracking().Select(h => h.DayOfWeek).Distinct().ToListAsync(cancellationToken);
            if (weekly.Count == 0) weekly.Add(DayOfWeek.Friday);
            int mask = 0;
            for (int d = 0; d < 7; d++) if (!weekly.Contains((DayOfWeek)d)) mask |= 1 << d;
            return new PcWorkCalendar(mask, holidays, null);
        }

        public static Task<PcWorkCalendar> CalendarForContractAsync(IUnitOfWork uow, PcContractTerms terms, CancellationToken cancellationToken) =>
            CalendarAsync(uow, terms?.CalendarId, cancellationToken);

        public static DateTime ScheduleStart(PcContractTerms terms, Entities.Model.NasbNiro.Contracts.ContractEntity contract)
        {
            if (terms?.ScheduleStartDate != null) return terms.ScheduleStartDate.Value.Date;
            DateTime? s = contract == null ? null : PcText.RealDate(contract.StartDate);
            return s ?? DateTime.Today;
        }
    }
}
