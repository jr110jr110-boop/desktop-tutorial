using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Entities.Model.NasbNiro.Quality;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.QualityDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Quality module (QC + QA). Role checks per step (segregation of duties) are in the services; reads are open to the quality readers.

    /// <summary>Inspection and test plans (WI-QC-004), inspection records and test reports.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality")]
    [RolesCheckedInService]
    public class QualityPlanController : ControllerBase
    {
        private readonly IItpService _itp;
        private readonly IInspectionService _inspection;

        public QualityPlanController(IItpService itp, IInspectionService inspection)
        {
            _itp = itp;
            _inspection = inspection;
        }

        // ITP templates and plans
        [HttpGet("itp-templates")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> TemplatesAsync([FromQuery] ItpTemplateQuery query, CancellationToken ct) => Ok((await _itp.GetTemplatesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("itp-templates/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> TemplateAsync(int id, CancellationToken ct) => Ok(await _itp.GetTemplateAsync(id, ct));

        [HttpGet("itps")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ItpsAsync([FromQuery] ItpQuery query, CancellationToken ct) => Ok((await _itp.GetItpsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("itps/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ItpAsync(int id, CancellationToken ct) => Ok(await _itp.GetItpAsync(id, ct));

        [HttpGet("itps/by-work-order/{workOrderId:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ItpByWorkOrderAsync(int workOrderId, CancellationToken ct) => Ok(await _itp.GetByWorkOrderAsync(workOrderId, ct));

        [HttpGet("itps/pending-points")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> PendingPointsAsync([FromQuery] PendingPointQuery query, CancellationToken ct) => Ok((await _itp.GetPendingPointsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("itps/ensure")]
        public async Task<IActionResult> EnsureItpAsync([FromBody] EnsureItpDto dto, CancellationToken ct) => Ok(await _itp.EnsureItpAsync(dto, ct));

        [HttpPost("itps")]
        public async Task<IActionResult> SaveItpAsync([FromBody] SaveItpDto dto, CancellationToken ct) => Ok(await _itp.SaveItpAsync(dto, ct));

        [HttpPost("itps/action")]
        public async Task<IActionResult> ItpActionAsync([FromBody] ItpActionDto dto, CancellationToken ct) => Ok(await _itp.ItpActionAsync(dto, ct));

        [HttpPost("itps/activity")]
        public async Task<IActionResult> SaveActivityAsync([FromBody] SaveItpActivityDto dto, CancellationToken ct) => Ok(await _itp.SaveActivityAsync(dto, ct));

        [HttpPost("itps/activity/action")]
        public async Task<IActionResult> ActivityActionAsync([FromBody] ItpActivityActionDto dto, CancellationToken ct) => Ok(await _itp.ActivityActionAsync(dto, ct));

        [HttpPost("itps/release")]
        public async Task<IActionResult> AddReleaseAsync([FromBody] SaveItpReleaseDto dto, CancellationToken ct) => Ok(await _itp.AddReleaseAsync(dto, ct));

        [HttpPost("itps/limits")]
        public async Task<IActionResult> SetLimitsAsync([FromBody] SetItpLimitsDto dto, CancellationToken ct) => Ok(await _itp.SetLimitsAsync(dto, ct));

        // inspection records
        [HttpGet("records")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> RecordsAsync([FromQuery] QcRecordQuery query, CancellationToken ct) => Ok((await _inspection.GetRecordsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("records/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> RecordAsync(int id, CancellationToken ct) => Ok(await _inspection.GetRecordAsync(id, ct));

        [HttpPost("records")]
        public async Task<IActionResult> SaveRecordAsync([FromBody] SaveQcRecordDto dto, CancellationToken ct) => Ok(await _inspection.SaveRecordAsync(dto, ct));

        // test reports
        [HttpGet("reports")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ReportsAsync([FromQuery] TestReportQuery query, CancellationToken ct) => Ok((await _inspection.GetReportsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("reports/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ReportAsync(int id, CancellationToken ct) => Ok(await _inspection.GetReportAsync(id, ct));

        [HttpGet("reports/standards/{type}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public IActionResult Standards(TestReportType type) => Ok(_inspection.GetStandards(type));

        [HttpPost("reports")]
        public async Task<IActionResult> SaveReportAsync([FromBody] SaveTestReportDto dto, CancellationToken ct) => Ok(await _inspection.SaveReportAsync(dto, ct));

        [HttpPost("reports/action")]
        public async Task<IActionResult> ReportActionAsync([FromBody] TestReportActionDto dto, CancellationToken ct) => Ok(await _inspection.ReportActionAsync(dto, ct));
    }

    /// <summary>Measuring equipment, calibration and out-of-tolerance impact cases (WI-QC-001).</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality")]
    [RolesCheckedInService]
    public class QualityCalibrationController : ControllerBase
    {
        private readonly ICalibrationService _service;

        public QualityCalibrationController(ICalibrationService service) => _service = service;

        [HttpGet("equipment")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> EquipmentAsync([FromQuery] EquipmentQuery query, CancellationToken ct) => Ok((await _service.GetEquipmentAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("equipment/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> EquipmentByIdAsync(int id, CancellationToken ct) => Ok(await _service.GetEquipmentAsync(id, ct));

        [HttpGet("equipment/due")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> DueAsync([FromQuery] DueQuery query, CancellationToken ct) => Ok((await _service.GetDueAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("equipment")]
        public async Task<IActionResult> SaveEquipmentAsync([FromBody] SaveEquipmentDto dto, CancellationToken ct) => Ok(await _service.SaveEquipmentAsync(dto, ct));

        [HttpPost("equipment/action")]
        public async Task<IActionResult> EquipmentActionAsync([FromBody] EquipmentActionDto dto, CancellationToken ct) => Ok(await _service.EquipmentActionAsync(dto, ct));

        [HttpPost("equipment/calibration")]
        public async Task<IActionResult> CalibrationAsync([FromBody] SaveCalibrationEventDto dto, CancellationToken ct) => Ok(await _service.AddEventAsync(dto, ct));

        [HttpGet("calibration/intervals")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> IntervalsAsync(CancellationToken ct) => Ok(await _service.GetIntervalsAsync(ct));

        [HttpPost("calibration/intervals")]
        public async Task<IActionResult> SaveIntervalAsync([FromBody] CalibrationIntervalDto dto, CancellationToken ct) => Ok(await _service.SaveIntervalAsync(dto, ct));

        [HttpGet("out-of-tolerance")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> CasesAsync([FromQuery] OutOfToleranceQuery query, CancellationToken ct) => Ok((await _service.GetCasesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("out-of-tolerance/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> CaseAsync(int id, CancellationToken ct) => Ok(await _service.GetCaseAsync(id, ct));

        [HttpPost("out-of-tolerance/assess")]
        public async Task<IActionResult> AssessAsync([FromBody] AssessOutOfToleranceDto dto, CancellationToken ct) => Ok(await _service.AssessAsync(dto, ct));

        /// <summary>R1q D40: links the accepted new inspection that re-inspected a document assessed as "re-inspect".</summary>
        [HttpPost("out-of-tolerance/reinspection")]
        public async Task<IActionResult> LinkReinspectionAsync([FromBody] LinkOotReinspectionDto dto, CancellationToken ct) => Ok(await _service.LinkReinspectionAsync(dto, ct));

        [HttpPost("out-of-tolerance/close")]
        public async Task<IActionResult> CloseCaseAsync([FromBody] CloseOutOfToleranceDto dto, CancellationToken ct) => Ok(await _service.CloseCaseAsync(dto, ct));
    }

    /// <summary>Qualified persons, qualifications, WPS/PQR and weld logs (WI-QC-010, ASME IX).</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality")]
    [RolesCheckedInService]
    public class QualityPersonnelController : ControllerBase
    {
        private readonly IPersonnelService _service;

        public QualityPersonnelController(IPersonnelService service) => _service = service;

        [HttpGet("persons")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> PersonsAsync([FromQuery] QualifiedPersonQuery query, CancellationToken ct) => Ok((await _service.GetPersonsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("persons/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> PersonAsync(int id, CancellationToken ct) => Ok(await _service.GetPersonAsync(id, ct));

        [HttpPost("persons")]
        public async Task<IActionResult> SavePersonAsync([FromBody] SavePersonDto dto, CancellationToken ct) => Ok(await _service.SavePersonAsync(dto, ct));

        [HttpGet("qualifications")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> QualificationsAsync([FromQuery] QualificationQuery query, CancellationToken ct) => Ok((await _service.GetQualificationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("qualifications/continuity")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ContinuityAsync([FromQuery] ContinuityQuery query, CancellationToken ct) => Ok((await _service.GetContinuityAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("qualifications")]
        public async Task<IActionResult> SaveQualificationAsync([FromBody] SaveQualificationDto dto, CancellationToken ct) => Ok(await _service.SaveQualificationAsync(dto, ct));

        [HttpPost("qualifications/action")]
        public async Task<IActionResult> QualificationActionAsync([FromBody] QualificationActionDto dto, CancellationToken ct) => Ok(await _service.QualificationActionAsync(dto, ct));

        [HttpGet("wps")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> WpsListAsync([FromQuery] WpsQuery query, CancellationToken ct) => Ok((await _service.GetWpsListAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("wps/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> WpsAsync(int id, CancellationToken ct) => Ok(await _service.GetWpsAsync(id, ct));

        [HttpPost("wps")]
        public async Task<IActionResult> SaveWpsAsync([FromBody] SaveWpsDto dto, CancellationToken ct) => Ok(await _service.SaveWpsAsync(dto, ct));

        [HttpPost("wps/action")]
        public async Task<IActionResult> WpsActionAsync([FromBody] WpsActionDto dto, CancellationToken ct) => Ok(await _service.WpsActionAsync(dto, ct));

        [HttpGet("pqrs")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> PqrsAsync(CancellationToken ct) => Ok(await _service.GetPqrsAsync(ct));

        [HttpPost("pqrs")]
        public async Task<IActionResult> SavePqrAsync([FromBody] SavePqrDto dto, CancellationToken ct) => Ok(await _service.SavePqrAsync(dto, ct));

        [HttpPost("pqrs/action")]
        public async Task<IActionResult> PqrActionAsync([FromBody] PqrActionDto dto, CancellationToken ct) => Ok(await _service.PqrActionAsync(dto, ct));

        [HttpGet("weld-logs")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> WeldLogsAsync([FromQuery] WeldLogQuery query, CancellationToken ct) => Ok((await _service.GetWeldLogsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("weld-logs")]
        public async Task<IActionResult> SaveWeldLogAsync([FromBody] SaveWeldLogDto dto, CancellationToken ct) => Ok(await _service.SaveWeldLogAsync(dto, ct));
    }

    /// <summary>EN 10204 material certificates and the traceability query.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality")]
    [RolesCheckedInService]
    public class QualityMaterialController : ControllerBase
    {
        private readonly IMaterialService _service;

        public QualityMaterialController(IMaterialService service) => _service = service;

        [HttpGet("certificates")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> CertificatesAsync([FromQuery] MaterialCertificateQuery query, CancellationToken ct) => Ok((await _service.GetCertificatesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("certificates")]
        public async Task<IActionResult> SaveCertificateAsync([FromBody] SaveMaterialCertificateDto dto, CancellationToken ct) => Ok(await _service.SaveCertificateAsync(dto, ct));

        [HttpPost("certificates/verify")]
        public async Task<IActionResult> VerifyAsync([FromBody] VerifyCertificateDto dto, CancellationToken ct) => Ok(await _service.VerifyCertificateAsync(dto, ct));

        [HttpGet("traceability")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> TraceAsync([FromQuery] TraceabilityQuery query, CancellationToken ct) => Ok(await _service.TraceAsync(query, ct));
    }

    /// <summary>CAPA, customer complaints, internal audits and management review.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality")]
    [RolesCheckedInService]
    public class QualityImprovementController : ControllerBase
    {
        private readonly ICapaService _capa;
        private readonly IAuditReviewService _audit;

        public QualityImprovementController(ICapaService capa, IAuditReviewService audit)
        {
            _capa = capa;
            _audit = audit;
        }

        // CAPA. R1q D13: any signed-in user may call the reads; the service shows the readers of the module everything and everybody else only the CAPAs he owns or has an action in
        [HttpGet("capas")]
        public async Task<IActionResult> CapasAsync([FromQuery] CapaQuery query, CancellationToken ct) => Ok((await _capa.GetCapasAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("capas/overdue")]
        public async Task<IActionResult> OverdueAsync([FromQuery] CapaQuery query, CancellationToken ct) => Ok((await _capa.GetOverdueAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("capas/{id:int}")]
        public async Task<IActionResult> CapaAsync(int id, CancellationToken ct) => Ok(await _capa.GetCapaAsync(id, ct));

        /// <summary>Runs the nightly late-CAPA escalation now (QA, PMO); the same method the 04:00 job calls.</summary>
        [HttpPost("capas/escalate")]
        [Authorize(Roles = "QA,PMO")]
        public async Task<IActionResult> EscalateAsync(CancellationToken ct) => Ok(ResponseDtoHelper.Ok(await _capa.EscalateAsync(ct)));

        [HttpPost("capas")]
        public async Task<IActionResult> SaveCapaAsync([FromBody] SaveCapaDto dto, CancellationToken ct) => Ok(await _capa.SaveCapaAsync(dto, ct));

        [HttpPost("capas/action")]
        public async Task<IActionResult> CapaActionAsync([FromBody] CapaActionRequestDto dto, CancellationToken ct) => Ok(await _capa.CapaActionAsync(dto, ct));

        // complaints
        [HttpGet("complaints")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ComplaintsAsync([FromQuery] ComplaintQuery query, CancellationToken ct) => Ok((await _capa.GetComplaintsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("complaints/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ComplaintAsync(int id, CancellationToken ct) => Ok(await _capa.GetComplaintAsync(id, ct));

        [HttpPost("complaints")]
        public async Task<IActionResult> SaveComplaintAsync([FromBody] SaveComplaintDto dto, CancellationToken ct) => Ok(await _capa.SaveComplaintAsync(dto, ct));

        [HttpPost("complaints/action")]
        public async Task<IActionResult> ComplaintActionAsync([FromBody] ComplaintActionDto dto, CancellationToken ct) => Ok(await _capa.ComplaintActionAsync(dto, ct));

        // audits
        [HttpGet("audit-programs")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ProgramsAsync(CancellationToken ct) => Ok(await _audit.GetProgramsAsync(ct));

        [HttpPost("audit-programs")]
        public async Task<IActionResult> SaveProgramAsync([FromBody] SaveAuditProgramDto dto, CancellationToken ct) => Ok(await _audit.SaveProgramAsync(dto, ct));

        [HttpGet("audits")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> AuditsAsync([FromQuery] AuditQuery query, CancellationToken ct) => Ok((await _audit.GetAuditsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("audits/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> AuditAsync(int id, CancellationToken ct) => Ok(await _audit.GetAuditAsync(id, ct));

        [HttpPost("audits")]
        public async Task<IActionResult> SaveAuditAsync([FromBody] SaveAuditDto dto, CancellationToken ct) => Ok(await _audit.SaveAuditAsync(dto, ct));

        [HttpPost("audits/findings")]
        public async Task<IActionResult> SaveFindingAsync([FromBody] SaveAuditFindingDto dto, CancellationToken ct) => Ok(await _audit.SaveFindingAsync(dto, ct));

        [HttpPost("audits/action")]
        public async Task<IActionResult> AuditActionAsync([FromBody] AuditActionDto dto, CancellationToken ct) => Ok(await _audit.AuditActionAsync(dto, ct));

        // management review
        [HttpGet("management-reviews")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ReviewsAsync([FromQuery] ReviewQuery query, CancellationToken ct) => Ok((await _audit.GetReviewsAsync(query, ct)).ToPaginatedResultSafe());

        /// <summary>Quality readers read any review; the person responsible for a decision of a closed review reads that review (the service filters it to his own decisions).</summary>
        [HttpGet("management-reviews/{id:int}")]
        public async Task<IActionResult> ReviewAsync(int id, CancellationToken ct) => Ok(await _audit.GetReviewAsync(id, ct));

        [HttpPost("management-reviews")]
        public async Task<IActionResult> CreateReviewAsync([FromBody] CreateReviewDto dto, CancellationToken ct) => Ok(await _audit.CreateReviewAsync(dto, ct));

        [HttpPost("management-reviews/input")]
        public async Task<IActionResult> SaveInputAsync([FromBody] SaveReviewInputDto dto, CancellationToken ct) => Ok(await _audit.SaveInputAsync(dto, ct));

        [HttpPost("management-reviews/decision")]
        public async Task<IActionResult> SaveDecisionAsync([FromBody] SaveReviewDecisionDto dto, CancellationToken ct) => Ok(await _audit.SaveDecisionAsync(dto, ct));

        [HttpPost("management-reviews/action")]
        public async Task<IActionResult> ReviewActionAsync([FromBody] ReviewActionDto dto, CancellationToken ct) => Ok(await _audit.ReviewActionAsync(dto, ct));

        /// <summary>R1q D23: done, cancel or reopen one decision of a closed review (the responsible person may report it done).</summary>
        [HttpPost("management-reviews/decision/action")]
        public async Task<IActionResult> DecisionActionAsync([FromBody] ReviewDecisionActionDto dto, CancellationToken ct) => Ok(await _audit.DecisionActionAsync(dto, ct));
    }

    /// <summary>Release notes and the final data book (ISO 8.6).</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality")]
    [RolesCheckedInService]
    public class QualityReleaseController : ControllerBase
    {
        private readonly IReleaseService _service;

        public QualityReleaseController(IReleaseService service) => _service = service;

        [HttpGet("release-notes")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> NotesAsync([FromQuery] ReleaseNoteQuery query, CancellationToken ct) => Ok((await _service.GetNotesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("release-notes/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> NoteAsync(int id, CancellationToken ct) => Ok(await _service.GetNoteAsync(id, ct));

        [HttpPost("release-notes")]
        public async Task<IActionResult> SaveNoteAsync([FromBody] SaveReleaseNoteDto dto, CancellationToken ct) => Ok(await _service.SaveNoteAsync(dto, ct));

        [HttpPost("release-notes/action")]
        public async Task<IActionResult> NoteActionAsync([FromBody] ReleaseNoteActionDto dto, CancellationToken ct) => Ok(await _service.NoteActionAsync(dto, ct));

        [HttpGet("work-orders/{workOrderId:int}/release-checks")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ChecksAsync(int workOrderId, CancellationToken ct) => Ok(await _service.GetChecksAsync(workOrderId, ct));

        [HttpGet("work-orders/{workOrderId:int}/data-book")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> DataBookAsync(int workOrderId, [FromQuery] string variant, CancellationToken ct) => Ok(await _service.GetDataBookAsync(workOrderId, variant, ct));
    }

    /// <summary>Document control, record retention, the quality dashboard and the company-changeable numbers.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality")]
    [RolesCheckedInService]
    public class QualityControlController : ControllerBase
    {
        private readonly IDocumentControlService _documents;
        private readonly IQualityDashboardService _dashboard;

        public QualityControlController(IDocumentControlService documents, IQualityDashboardService dashboard)
        {
            _documents = documents;
            _dashboard = dashboard;
        }

        [HttpGet("documents")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> DocumentsAsync([FromQuery] ControlledDocumentQuery query, CancellationToken ct) => Ok((await _documents.GetDocumentsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("documents/master-list")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> MasterListAsync([FromQuery] ControlledDocumentQuery query, CancellationToken ct) => Ok((await _documents.GetMasterListAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("documents/check-code")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> CheckCodeAsync([FromQuery] ControlledDocumentType type, [FromQuery] string department, [FromQuery] int? serial, CancellationToken ct) =>
            Ok(await _documents.CheckCodeAsync(type, department, serial, ct));

        [HttpGet("documents/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> DocumentAsync(int id, CancellationToken ct) => Ok(await _documents.GetDocumentAsync(id, ct));

        [HttpPost("documents")]
        public async Task<IActionResult> SaveDocumentAsync([FromBody] SaveControlledDocumentDto dto, CancellationToken ct) => Ok(await _documents.SaveDocumentAsync(dto, ct));

        [HttpPost("documents/revisions")]
        public async Task<IActionResult> SaveRevisionAsync([FromBody] SaveRevisionDto dto, CancellationToken ct) => Ok(await _documents.SaveRevisionAsync(dto, ct));

        [HttpPost("documents/revisions/action")]
        public async Task<IActionResult> RevisionActionAsync([FromBody] RevisionActionDto dto, CancellationToken ct) => Ok(await _documents.RevisionActionAsync(dto, ct));

        [HttpGet("retention")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> RetentionAsync(CancellationToken ct) => Ok(await _documents.GetRetentionAsync(ct));

        [HttpPost("retention")]
        public async Task<IActionResult> SaveRetentionAsync([FromBody] SaveRetentionRuleDto dto, CancellationToken ct) => Ok(await _documents.SaveRetentionAsync(dto, ct));

        [HttpGet("dashboard")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> DashboardAsync(CancellationToken ct) => Ok(await _dashboard.GetDashboardAsync(ct));

        [HttpGet("settings")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> SettingsAsync(CancellationToken ct) => Ok(await _dashboard.GetSettingsAsync(ct));

        [HttpPost("settings")]
        public async Task<IActionResult> SaveSettingAsync([FromBody] SaveQualitySettingDto dto, CancellationToken ct) => Ok(await _dashboard.SaveSettingAsync(dto, ct));
    }
}
