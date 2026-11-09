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
        /// <summary>
        /// Everybody who may read the quality records. R1q D27 added the auditor, the production and shop managers, maintenance, service,
        /// the project manager and the document controller: they work with controlled documents, NCRs and calibration every day.
        /// </summary>
        public const string Readers = "PMO,CEO,QC,QA,ENGINEERING,ENGINEER,CONSTRUCTION,ASSEMBLER,PLANNING,WAREHOUSE,WHSUPERVISOR,COMMERCIAL,AUDITOR,PRODUCTIONMANAGER,SHOPSUPERVISOR,MAINTENANCE,SERVICE,PROJECTMANAGER,DCC";

        /// <summary>The QA manager: approves what is relaxed (calibration waiver, cancelling a mandatory CAPA) and may cancel an NCR.</summary>
        public const string QaManager = "QA";

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

        /// <summary>Who may upload the file that proves an inspection and test plan activity without a test report was done (review M16).</summary>
        public const string EvidenceUploaders = "QC,QA,ENGINEERING";
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
        public const int FirstArticle = 410;
        public const int SpQualification = 411;
        public const int SpRun = 412;
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

        // ----- R1q
        /// <summary>How many days back an inspection record, test report or weld log may be dated (D15).</summary>
        public const string MaxBackdateDays = "MaxBackdateDays";

        /// <summary>Days after a CAPA was implemented before its effectiveness may be judged (D20).</summary>
        public const string EffectivenessWaitDays = "EffectivenessWaitDays";

        /// <summary>Decision deadline of an NCR raised by an incoming inspection (D34).</summary>
        public const string IncomingNcrDueDays = "IncomingNcrDueDays";

        /// <summary>Days a CAPA is late before the CEO is told, after QA was told on the first late day (D13).</summary>
        public const string CapaCeoEscalationDays = "CapaCeoEscalationDays";

        /// <summary>Lots expiring within this many days are on the expiry report (D32).</summary>
        public const string ExpiryWarnDays = "ExpiryWarnDays";

        // ---- PB5 (warehouse completion); every default is an assumption of the answers table (★) the company confirms
        /// <summary>Consignment goods go back to the owner within this many days of the receipt when no due date is typed.</summary>
        public const string ConsignmentReturnDays = "ConsignmentReturnDays";

        /// <summary>First and second alert before the consignment due date (days).</summary>
        public const string ConsignmentAlertDays1 = "ConsignmentAlertDays1";
        public const string ConsignmentAlertDays2 = "ConsignmentAlertDays2";

        /// <summary>A pending incoming inspection older than this many working days is reported to QC; older than the second value it goes to the QA manager and planning.</summary>
        public const string QcPendingWorkDays = "QcPendingWorkDays";
        public const string QcEscalateWorkDays = "QcEscalateWorkDays";

        /// <summary>Stock without movement for this many days is slow, stagnant, dead.</summary>
        public const string StagnantSlowDays = "StagnantSlowDays";
        public const string StagnantDays = "StagnantDays";
        public const string StagnantDeadDays = "StagnantDeadDays";

        /// <summary>The period of past issues the average daily use of the reorder point is taken from.</summary>
        public const string ReorderUseDays = "ReorderUseDays";

        /// <summary>The shortest witness notice (days) an ITP may be given (D9, review H6): a notice of 0 days would let the client's witness be waived at once.</summary>
        public const string MinNoticeDays = "MinNoticeDays";

        // ---- Package B (inspection and release integrity); every default is a strict ★ the company confirms and every key is security-relevant (two-person change, see SafetyProfile)
        /// <summary>Percentage of the pieces measured for a critical / major / minor characteristic when the list does not say otherwise (a critical characteristic is never measured on less than this).</summary>
        public const string PkbSampleCritical = "Pkb.Char.SamplePercent.Critical";
        public const string PkbSampleMajor = "Pkb.Char.SamplePercent.Major";
        public const string PkbSampleMinor = "Pkb.Char.SamplePercent.Minor";

        /// <summary>1: the first article inspection is enforced (flagged routing steps and the first order after a new revision).</summary>
        public const string PkbFaiEnforce = "Pkb.Fai.Enforce";

        /// <summary>1: the first production order of a part after a new released drawing / BOM / routing revision needs a first article inspection.</summary>
        public const string PkbFaiOnNewRevision = "Pkb.Fai.OnNewRevision";

        /// <summary>1: the physical configuration audit runs before a product is released.</summary>
        public const string PkbPcaEnabled = "Pkb.Pca.Enabled";

        /// <summary>The quantity of a project BOM line in the genealogy may differ from the BOM by this percent (0 = exact; zero tolerance).</summary>
        public const string PkbPcaTolerance = "Pkb.Pca.QtyTolerancePercent";

        /// <summary>The shortest reason (characters) of a «بدون نقشه» flag.</summary>
        public const string PkbNoDrawingReasonMin = "Pkb.NoDrawing.ReasonMinChars";

        /// <summary>Incoming inspection of a bulk lot (kg, m, l): the number of samples per lot or heat (a bulk lot is never sampled per kilogram).</summary>
        public const string PkbIncomingBulkSamples = "Pkb.Incoming.BulkSamplesPerLot";

        /// <summary>Percent added to the BOM scrap allowance of a bulk line in the physical configuration audit (0 = exactly the BOM allowance).</summary>
        public const string PkbPcaBulkExtra = "Pkb.Pca.BulkExtraTolerancePercent";

        // ---- Package C (special processes and material verification); every key is security-relevant (two-person change, see SafetyProfile)
        /// <summary>1: a special process step (flagged on the operation standard) is finished only with a passed, QA-reviewed run under a valid qualification, by a qualified operator, on calibrated equipment.</summary>
        public const string PkcSpEnforce = "Pkc.SpecialProcess.Enforce";

        /// <summary>1: a subcontracted special process step needs the run evidence (certificate and chart of the supplier) before QC accepts the receipt and before the step is completed.</summary>
        public const string PkcSpSubcontractEvidence = "Pkc.SpecialProcess.SubcontractEvidence";

        /// <summary>QA and engineering are told this many days before a special process qualification or a supplier approval expires.</summary>
        public const string PkcQualExpiryAlertDays = "Pkc.Qualification.ExpiryAlertDays";

        /// <summary>1: a welder is booked only inside the qualified range (P-No, group, thickness, diameter, position, filler F-No) of the WPS.</summary>
        public const string PkcWeldRange = "Pkc.Weld.RangeEnforce";

        /// <summary>1: the values of a type 3.1 / 3.2 certificate are judged against the material specification and the receipt is not accepted on a certificate that fails.</summary>
        public const string PkcCertEnforce = "Pkc.Certificate.Enforce";

        /// <summary>
        /// Not a setting of the company: a marker the nightly CAPA job writes after its first run (review M11). Until it exists the job records the escalation level of the CAPAs
        /// that were already late without telling anybody, so that the first night after the upgrade does not flood QA and the CEO with the whole backlog.
        /// </summary>
        public const string CapaEscalationBootstrapped = "CapaEscalationBootstrapped";

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
            yield return new QualitySetting { Key = MaxBackdateDays, Value = 7, Title = "حداکثر تاخیر مجاز در ثبت بازرسی، گزارش آزمون و گزارش جوشکاری (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = EffectivenessWaitDays, Value = 30, Title = "حداقل فاصله اجرای اقدام اصلاحی تا بررسی اثربخشی (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = IncomingNcrDueDays, Value = 3, Title = "مهلت تصمیم عدم انطباق بازرسی ورودی (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = CapaCeoEscalationDays, Value = 14, Title = "دیرکرد CAPA تا اطلاع به مدیرعامل (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = ExpiryWarnDays, Value = 30, Title = "هشدار انقضای بچ‌ها (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = MinNoticeDays, Value = 1, Title = "حداقل روزهای اعلام نقطه شاهد در برنامه بازرسی (روز)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = ConsignmentReturnDays, Value = 30, Title = "مهلت پیش‌فرض عودت کالای امانی (روز)", Source = "★ جدول پاسخ‌های شرکت" };
            yield return new QualitySetting { Key = ConsignmentAlertDays1, Value = 7, Title = "هشدار اول عودت کالای امانی (روز مانده به سررسید)", Source = "★ جدول پاسخ‌های شرکت" };
            yield return new QualitySetting { Key = ConsignmentAlertDays2, Value = 3, Title = "هشدار دوم عودت کالای امانی (روز مانده به سررسید)", Source = "★ جدول پاسخ‌های شرکت" };
            yield return new QualitySetting { Key = QcPendingWorkDays, Value = 3, Title = "مهلت اعلام نتیجه بازرسی ورودی (روز کاری)", Source = "★ جدول پاسخ‌های شرکت" };
            yield return new QualitySetting { Key = QcEscalateWorkDays, Value = 5, Title = "ارجاع بازرسی معوق به مدیر تضمین کیفیت و برنامه‌ریزی (روز کاری)", Source = "★ جدول پاسخ‌های شرکت" };
            yield return new QualitySetting { Key = StagnantSlowDays, Value = 60, Title = "کالای کندگردش: بدون گردش از (روز)", Source = "★ جدول پاسخ‌های شرکت" };
            yield return new QualitySetting { Key = StagnantDays, Value = 90, Title = "کالای راکد: بدون گردش از (روز)", Source = "★ جدول پاسخ‌های شرکت" };
            yield return new QualitySetting { Key = StagnantDeadDays, Value = 180, Title = "کالای راکد مرده: بدون گردش از (روز)", Source = "★ جدول پاسخ‌های شرکت" };
            yield return new QualitySetting { Key = ReorderUseDays, Value = 90, Title = "بازه محاسبه مصرف روزانه برای نقطه سفارش (روز)", Source = "★ پیشنهاد PMIS" };
            yield return new QualitySetting { Key = PkbSampleCritical, Value = 100, Title = "نمونه‌برداری مشخصه بحرانی: درصد قطعات اندازه‌گیری‌شده", Source = "★ تصمیم مدیرعامل (۱۰۰٪ برای بحرانی)" };
            yield return new QualitySetting { Key = PkbSampleMajor, Value = 100, Title = "نمونه‌برداری مشخصه عمده: درصد قطعات اندازه‌گیری‌شده", Source = "★ تصمیم موقت (عدم تحمل انحراف)" };
            yield return new QualitySetting { Key = PkbSampleMinor, Value = 10, Title = "نمونه‌برداری مشخصه جزئی: درصد قطعات اندازه‌گیری‌شده", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = PkbFaiEnforce, Value = 1, Title = "بازرسی نمونه اول اجباری باشد", Source = "★ تصمیم مدیرعامل" };
            yield return new QualitySetting { Key = PkbFaiOnNewRevision, Value = 1, Title = "نمونه اول برای اولین دستور پس از بازنگری تازه نقشه، BOM یا مسیر ساخت", Source = "★ تصمیم مدیرعامل" };
            yield return new QualitySetting { Key = PkbPcaEnabled, Value = 1, Title = "ممیزی پیکربندی فیزیکی پیش از ترخیص محصول", Source = "★ تصمیم مدیرعامل (ISO 10007)" };
            yield return new QualitySetting { Key = PkbPcaTolerance, Value = 0, Title = "رواداری مقدار ردیف BOM پروژه در ممیزی پیکربندی (درصد)", Source = "★ عدم تحمل انحراف" };
            yield return new QualitySetting { Key = PkbNoDrawingReasonMin, Value = 15, Title = "حداقل نویسه دلیل پرچم «بدون نقشه»", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = PkbIncomingBulkSamples, Value = 1, Title = "ورودی انبوه (کیلوگرم، متر، لیتر): تعداد نمونه برای هر بچ یا ذوب", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = PkbPcaBulkExtra, Value = 0, Title = "رواداری افزوده بر ضریب ضایعات BOM برای مواد انبوه در ممیزی پیکربندی (درصد)", Source = "★ ضریب ضایعات BOM" };
            yield return new QualitySetting { Key = PkcSpEnforce, Value = 1, Title = "پایان گام فرایند ویژه فقط با اجرای قبول و بازبینی‌شده QA، تحت صلاحیت معتبر", Source = "★ تصمیم مدیرعامل (ISO 9001 بند 8.5.1 و عدم تحمل انحراف)" };
            yield return new QualitySetting { Key = PkcSpSubcontractEvidence, Value = 1, Title = "گام فرایند ویژه برون‌سپاری: گواهی و نمودار تامین‌کننده پیش از پذیرش QC", Source = "★ تصمیم مدیرعامل" };
            yield return new QualitySetting { Key = PkcQualExpiryAlertDays, Value = 30, Title = "اطلاع انقضای صلاحیت فرایند ویژه و تایید تامین‌کننده (روز مانده)", Source = "★ تصمیم موقت" };
            yield return new QualitySetting { Key = PkcWeldRange, Value = 1, Title = "کنترل دامنه صلاحیت جوشکار نسبت به WPS", Source = "★ ASME IX QW-451 تا QW-461" };
            yield return new QualitySetting { Key = PkcCertEnforce, Value = 1, Title = "سنجش مقادیر گواهی 3.1/3.2 با مشخصات ماده و جلوگیری از پذیرش رسید", Source = "★ تصمیم مدیرعامل (EN 10204)" };
            // PB8: operational data control (plausibility and baseline-conformance rules of the gates and the OPS-DATA seat)
            foreach (QualitySetting od in global::Services.NasbNiro.OpsData.OpsRuleCatalog.Defaults()) yield return od;
            foreach (QualitySetting dd in global::Services.NasbNiro.ProductBank.DesignSettingKeys.Defaults()) yield return dd;   // Package A1
            foreach (QualitySetting ad in global::Services.NasbNiro.AuditSeat.AusSettingKeys.Defaults()) yield return ad;   // Package 9
            foreach (QualitySetting gd in global::Services.NasbNiro.GoLive.GlSettingKeys.Defaults()) yield return gd;   // Package G
        }

        /// <summary>
        /// Stored values over the defaults, each forced into its range (review M14). Reading never writes, so it is safe in the middle of an operation.
        /// </summary>
        public static async Task<Dictionary<string, decimal>> LoadAsync(IUnitOfWork uow, CancellationToken cancellationToken)
        {
            Dictionary<string, decimal> result = Defaults().ToDictionary(d => d.Key, d => d.Value);
            List<QualitySetting> rows = await uow.Set<QualitySetting>().AsNoTracking().ToListAsync(cancellationToken);
            foreach (QualitySetting row in rows)
                if (result.ContainsKey(row.Key)) result[row.Key] = QualitySettingRules.Clamp(row.Key, row.Value);
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
