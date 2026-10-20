using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using DataLayer.Context;
using Entities.Model.NasbNiro.Quality;
using Microsoft.EntityFrameworkCore;

namespace Services.NasbNiro.Quality
{
    /// <summary>Role names (as written in the JWT, upper case) for the quality module.</summary>
    public static class QualityRoles
    {
        /// <summary>QC inspectors: only QC inspects, tests and calibrates (segregation from production).</summary>
        public const string Inspectors = "QC";

        public const string QualityManagers = "QC,QA,PMO";
        public const string Engineering = "ENGINEERING,ENGINEER";
        public const string Readers = "PMO,CEO,QC,QA,ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER,PLANNING,WAREHOUSE,WHSUPERVISOR,COMMERCIAL";

        /// <summary>Plans and documents are prepared by quality and engineering.</summary>
        public const string Planners = "QC,QA,PMO,ENGINEERING,ENGINEER";

        /// <summary>Operators may check their own work on activities that are not on the ITP (self-inspection).</summary>
        public const string RecordWriters = "QC,PMO,ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER";

        /// <summary>CAPA administration and closing: quality assurance or the QC manager.</summary>
        public const string CapaClosers = "QA,QC";

        /// <summary>Material certificates may be attached by the warehouse at receipt; only QC verifies them.</summary>
        public const string CertificateWriters = "QC,QA,PMO,WAREHOUSE,WHSUPERVISOR,ENGINEERING";

        /// <summary>Approves a controlled document revision.</summary>
        public const string DocumentApprovers = "QA,PMO,CEO";

        /// <summary>Reviews a controlled document revision.</summary>
        public const string DocumentReviewers = "QA,QC,PMO,ENGINEERING,ENGINEER";

        public const string Audit = "QA,PMO";
        public const string Complaints = "QA,QC,PMO,COMMERCIAL";
    }

    /// <summary>Counter kinds in <c>DocumentNumberSequence</c> (the quality module uses 401..420).</summary>
    internal static class QualityNumbers
    {
        public const int Record = 401;
        public const int TestReport = 402;
        public const int Equipment = 403;
        public const int Capa = 404;
        public const int Complaint = 405;
        public const int ReleaseNote = 406;
        public const int Audit = 407;
        public const int Itp = 408;
        public const int Oot = 409;
    }

    /// <summary>Numbers the company can change. Defaults come from the procedures and the architect's provisional decisions (★).</summary>
    public static class QualitySettingKeys
    {
        public const string NcrMinorDays = "NcrMinorDueDays";
        public const string NcrMajorDays = "NcrMajorDueDays";
        public const string CapaEffectivenessDays = "CapaEffectivenessDays";
        public const string ComplaintResponseDays = "ComplaintResponseDays";
        public const string NoticeDaysIran = "ItpNoticeDaysIran";
        public const string NoticeDaysAbroad = "ItpNoticeDaysAbroad";
        public const string WeldContinuityMonths = "WeldContinuityMonths";
        public const string CalibrationWarnDays = "CalibrationWarnDays";
        public const string ContinuityWarnDays = "ContinuityWarnDays";
        public const string MajorNcrCommitteeMin = "MajorNcrCommitteeMin";

        public static IEnumerable<QualitySetting> Defaults()
        {
            yield return new QualitySetting { Key = NcrMinorDays, Value = 7, Title = "مهلت تصمیم عدم انطباق جزئی (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = NcrMajorDays, Value = 14, Title = "مهلت تصمیم عدم انطباق عمده (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = CapaEffectivenessDays, Value = 90, Title = "فاصله بررسی اثربخشی پس از اجرای اقدام (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = ComplaintResponseDays, Value = 10, Title = "مهلت پاسخ به شکایت مشتری (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = NoticeDaysIran, Value = 10, Title = "اطلاع‌رسانی نقطه شاهد داخل ایران (روز)", Source = "WI-QC-004" };
            yield return new QualitySetting { Key = NoticeDaysAbroad, Value = 60, Title = "اطلاع‌رسانی نقطه شاهد خارج از ایران (روز)", Source = "WI-QC-004" };
            yield return new QualitySetting { Key = WeldContinuityMonths, Value = 6, Title = "حداکثر وقفه جوشکاری در یک فرایند (ماه)", Source = "ASME IX QW-322" };
            yield return new QualitySetting { Key = CalibrationWarnDays, Value = 30, Title = "هشدار سررسید کالیبراسیون (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = ContinuityWarnDays, Value = 30, Title = "هشدار انقضای تداوم جوشکار (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = MajorNcrCommitteeMin, Value = 3, Title = "حداقل اعضای کمیته برای عدم انطباق عمده", Source = "★ تصمیم موقت" };
        }

        /// <summary>Stored values over the defaults. Reading never writes, so it is safe in the middle of an operation.</summary>
        public static async Task<Dictionary<string, decimal>> LoadAsync(IUnitOfWork uow, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> result = Defaults().ToDictionary(d => d.Key, d => d.Value);
            List<QualitySetting> rows = await uow.Set<QualitySetting>().AsNoTracking().ToListAsync(cancellationToken);
            foreach (QualitySetting row in rows) result[row.Key] = row.Value;
            return result;
        }
    }

    internal static class QualityText
    {
        public static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        public static ResponseDto<T> Bad<T>(string message) => ResponseDtoHelper.BadRequest<T>(message);
        public static ResponseDto<T> NotFound<T>(string message) => ResponseDtoHelper.NotFound<T>(message);

        public const string NotAllowed = "شما مجوز این کار را ندارید.";
        public const string InvalidData = "اطلاعات ارسال‌شده کامل نیست.";
        public const string Concurrency = "اطلاعات هم‌زمان توسط کاربر دیگری تغییر کرد. صفحه را دوباره باز کنید و دوباره تلاش کنید.";
        public const string SameUser = "این کار باید توسط شخص دیگری انجام شود (تفکیک وظایف).";

        /// <summary>Upper-case method or process name, for example "gtaw" becomes "GTAW".</summary>
        public static string Upper(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

        /// <summary>Splits a list typed as lines or separated by commas (Latin or Persian), semicolons or bars.</summary>
        public static List<string> SplitList(string text) =>
            string.IsNullOrWhiteSpace(text)
                ? new List<string>()
                : text.Split(new[] { '\n', '\r', ',', '،', ';', '؛', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    /// <summary>
    /// Limits that come from the procedures (Appendix B of the quality specification) and the codes named in the brief.
    /// Where a limit is not known the row has <c>RequiresLimits</c>: the value must come from the ITP, set by engineering.
    /// </summary>
    public class StandardParameter
    {
        public string Name { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public string Source { get; set; }

        /// <summary>The report cannot be submitted without this row.</summary>
        public bool Mandatory { get; set; }

        /// <summary>The limit is not known: it is taken from the ITP activity parameter set by engineering.</summary>
        public bool RequiresLimits { get; set; }
    }

    public static class QualityStandards
    {
        public const string EngineeringToSet = "توسط مهندسی تعیین شود";

        private static StandardParameter P(string name, string unit, decimal? min, decimal? max, string source, bool mandatory = false) =>
            new StandardParameter { Name = name, Unit = unit, Min = min, Max = max, Source = source, Mandatory = mandatory };

        private static StandardParameter Open(string name, string unit, string source, bool mandatory = true) =>
            new StandardParameter { Name = name, Unit = unit, Source = source + " — " + EngineeringToSet, Mandatory = mandatory, RequiresLimits = true };

        public const string PtSource = "WI-QC-002 / ASME V Art.6";
        public const string MtSource = "WI-QC-008 / ASME V Art.7";
        public const string RunSource = "WI-QC-006 / API 611 6.3.3";

        public static List<StandardParameter> For(TestReportType type)
        {
            switch (type)
            {
                case TestReportType.PT:
                    return new List<StandardParameter>
                    {
                        P("Part and penetrant temperature", "°C", 5, 52, PtSource, true),
                        P("Dwell time", "min", null, 10, PtSource + " (maximum ten minutes, as printed)", true),
                        P("Interpretation time after developer", "min", 10, 60, PtSource, true),
                        P("White light intensity", "lux", 1000, null, PtSource, true),
                        P("UV intensity", null, 100, null, PtSource + " (unit not printed in the procedure)"),
                        P("Dark adaptation time", "min", 5, null, PtSource),
                        P("Wash water temperature", "°C", null, 43, PtSource),
                        P("Wash water pressure", "psi", null, 50, PtSource),
                    };
                case TestReportType.MT:
                    return new List<StandardParameter>
                    {
                        P("Yoke lifting force", "lb", 10, null, MtSource, true),
                        P("Residual magnetism after demagnetisation", "gauss", null, 3, MtSource + " (API 687 8.2.4.2: 2 gauss)", true),
                        P("White light intensity", "lux", 1000, null, MtSource),
                        P("Suspension temperature", "°C", null, 43, MtSource),
                        P("Ambient temperature", "°C", null, 57, MtSource),
                        P("Surface-to-suspension temperature difference", "°C", null, 14, MtSource),
                        P("Non-magnetic coating thickness", "in", null, 0.004m, MtSource),
                        P("Contrast paint thickness", "in", null, 0.002m, MtSource),
                        P("Particle concentration (visible)", "%", 1.2m, 2.4m, MtSource),
                        P("Particle concentration (fluorescent)", "%", 0.1m, 0.4m, MtSource),
                        P("Ambient light (fluorescent method)", "lux", null, 21.5m, MtSource),
                    };
                case TestReportType.RunTest:
                    return new List<StandardParameter>
                    {
                        P("Overspeed trip 1", "% of continuous speed", 107.8m, 112.2m, RunSource + ": 110 % ± 2 %", true),
                        P("Overspeed trip 2", "% of continuous speed", 107.8m, 112.2m, RunSource + ": 110 % ± 2 %", true),
                        P("Overspeed trip 3", "% of continuous speed", 107.8m, 112.2m, RunSource + ": 110 % ± 2 %", true),
                        P("Continuous run duration", "h", 4, null, RunSource, true),
                        P("Oil filtration", "micron", null, 25, RunSource),
                    };
                case TestReportType.Hydro:
                    return new List<StandardParameter>
                    {
                        Open("Test pressure", null, "API 611 6.3 / API 610 8.3 / ASME VIII-1 UG-99"),
                        Open("Hold time", "min", "API 611 6.3 / API 610 8.3 / ASME VIII-1 UG-99"),
                    };
                case TestReportType.Balancing:
                    return new List<StandardParameter>
                    {
                        Open("Balance quality grade achieved (G)", "mm/s", "ISO 21940-11 (replaces ISO 1940-1)"),
                    };
                default:
                    return new List<StandardParameter>();
            }
        }

        /// <summary>The NDT method a report type needs a qualified examiner for.</summary>
        public static string NdtMethod(TestReportType type)
        {
            switch (type)
            {
                case TestReportType.PT: return "PT";
                case TestReportType.MT: return "MT";
                case TestReportType.UT: return "UT";
                case TestReportType.RT: return "RT";
                default: return null;
            }
        }
    }
}
