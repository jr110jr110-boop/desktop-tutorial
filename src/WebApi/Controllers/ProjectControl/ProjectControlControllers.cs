using Common.Results;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProjectControlDtos;
using WebApi.Extensions;

namespace WebApi.Controllers.ProjectControl
{
    // Project control (PMO) module. Every write checks its role inside the service (segregation of duties); reads are open to the module readers.

    /// <summary>Contract terms, project coding, initiation, charter, project plan, contract review and amendments.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol")]
    public class ProjectControlSetupController : ControllerBase
    {
        private readonly IPcProjectService _project;

        public ProjectControlSetupController(IPcProjectService project) { _project = project; }

        [HttpGet("terms/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> TermsAsync(int contractId, CancellationToken ct) => Ok(await _project.GetTermsAsync(contractId, ct));

        [HttpPost("terms")]
        public async Task<IActionResult> SaveTermsAsync([FromBody] PcContractTermsDto dto, CancellationToken ct) => Ok(await _project.SaveTermsAsync(dto, ct));

        [HttpPost("terms/generate-code")]
        public async Task<IActionResult> GenerateCodeAsync([FromBody] GenerateProjectCodeDto dto, CancellationToken ct) => Ok(await _project.GenerateProjectCodeAsync(dto, ct));

        [HttpGet("coding/{table}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> CodingAsync(string table, CancellationToken ct) => Ok(await _project.GetCodingAsync(table, ct));

        [HttpPost("coding")]
        public async Task<IActionResult> SaveCodingAsync([FromBody] PcCodingRowDto dto, CancellationToken ct) => Ok(await _project.SaveCodingAsync(dto, ct));

        [HttpGet("work-order-info/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> WorkOrderInfosAsync(int contractId, CancellationToken ct) => Ok(await _project.GetWorkOrderInfosAsync(contractId, ct));

        [HttpPost("work-order-info")]
        public async Task<IActionResult> SaveWorkOrderInfoAsync([FromBody] PcWorkOrderInfoDto dto, CancellationToken ct) => Ok(await _project.SaveWorkOrderInfoAsync(dto, ct));

        [HttpGet("initiation/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> InitiationAsync(int contractId, CancellationToken ct) => Ok(await _project.GetInitiationAsync(contractId, ct));

        [HttpPost("initiation/start/{contractId:int}")]
        public async Task<IActionResult> StartInitiationAsync(int contractId, CancellationToken ct) => Ok(await _project.StartInitiationAsync(contractId, ct));

        [HttpPost("initiation/action")]
        public async Task<IActionResult> InitiationActionAsync([FromBody] PcInitiationActionDto dto, CancellationToken ct) => Ok(await _project.InitiationActionAsync(dto, ct));

        [HttpGet("initiation/overdue")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> OverdueStepsAsync(CancellationToken ct) => Ok(await _project.OverdueStepsAsync(ct));

        [HttpGet("charter/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> CharterAsync(int contractId, CancellationToken ct) => Ok(await _project.GetCharterAsync(contractId, ct));

        [HttpPost("charter")]
        public async Task<IActionResult> SaveCharterAsync([FromBody] PcCharterDto dto, CancellationToken ct) => Ok(await _project.SaveCharterAsync(dto, ct));

        [HttpPost("charter/{step}")]
        public async Task<IActionResult> CharterActionAsync(string step, [FromBody] PcApproveDto dto, CancellationToken ct) => Ok(await _project.CharterActionAsync(step, dto, ct));

        [HttpGet("plan/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> PlanAsync(int contractId, CancellationToken ct) => Ok(await _project.GetPlanAsync(contractId, ct));

        [HttpPost("plan")]
        public async Task<IActionResult> SavePlanAsync([FromBody] PcPlanDto dto, CancellationToken ct) => Ok(await _project.SavePlanAsync(dto, ct));

        [HttpPost("plan/approve")]
        public async Task<IActionResult> ApprovePlanAsync([FromBody] PcApproveDto dto, CancellationToken ct) => Ok(await _project.ApprovePlanAsync(dto, ct));

        [HttpGet("reviews")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ReviewsAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _project.GetReviewsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("reviews")]
        public async Task<IActionResult> SaveReviewAsync([FromBody] PcReviewDto dto, CancellationToken ct) => Ok(await _project.SaveReviewAsync(dto, ct));

        [HttpPost("reviews/action")]
        public async Task<IActionResult> ReviewActionAsync([FromBody] PcReviewActionDto dto, CancellationToken ct) => Ok(await _project.ReviewActionAsync(dto, ct));

        [HttpGet("amendments")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> AmendmentsAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _project.GetAmendmentsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("amendments")]
        public async Task<IActionResult> SaveAmendmentAsync([FromBody] PcAmendmentDto dto, CancellationToken ct) => Ok(await _project.SaveAmendmentAsync(dto, ct));

        [HttpPost("amendments/action")]
        public async Task<IActionResult> AmendmentActionAsync([FromBody] PcAmendmentActionDto dto, CancellationToken ct) => Ok(await _project.AmendmentActionAsync(dto, ct));
    }

    /// <summary>Calendars, WBS, activities, dependencies, critical path, executive plan, need-by dates and the MS Project export.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol")]
    public class ProjectControlScheduleController : ControllerBase
    {
        private readonly IPcScheduleService _schedule;

        public ProjectControlScheduleController(IPcScheduleService schedule) { _schedule = schedule; }

        [HttpGet("calendars")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> CalendarsAsync([FromQuery] PaginationParams query, CancellationToken ct) => Ok((await _schedule.GetCalendarsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("calendars")]
        public async Task<IActionResult> SaveCalendarAsync([FromBody] PcCalendarDto dto, CancellationToken ct) => Ok(await _schedule.SaveCalendarAsync(dto, ct));

        [HttpGet("wbs")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> WbsAsync([FromQuery] PcWbsQuery query, CancellationToken ct) => Ok((await _schedule.GetWbsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("wbs")]
        public async Task<IActionResult> SaveWbsAsync([FromBody] PcWbsNodeDto dto, CancellationToken ct) => Ok(await _schedule.SaveWbsNodeAsync(dto, ct));

        [HttpDelete("wbs/{id:int}")]
        public async Task<IActionResult> DeleteWbsAsync(int id, CancellationToken ct) => Ok(await _schedule.DeleteWbsNodeAsync(id, ct));

        [HttpPost("wbs/{id:int}/approve")]
        public async Task<IActionResult> ApproveWbsAsync(int id, CancellationToken ct) => Ok(await _schedule.ApproveWbsNodeAsync(id, ct));

        [HttpPost("wbs/generate")]
        public async Task<IActionResult> GenerateWbsAsync([FromBody] PcWbsGenerateDto dto, CancellationToken ct) => Ok(await _schedule.GenerateWbsAsync(dto, ct));

        [HttpPost("wbs/auto-weights/{contractId:int}")]
        public async Task<IActionResult> AutoWeightsAsync(int contractId, CancellationToken ct) => Ok(await _schedule.AutoWeightsAsync(contractId, ct));

        [HttpGet("activities")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ActivitiesAsync([FromQuery] PcActivityQuery query, CancellationToken ct) => Ok((await _schedule.GetActivitiesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("activities/{id:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ActivityAsync(int id, CancellationToken ct) => Ok(await _schedule.GetActivityAsync(id, ct));

        [HttpPost("activities")]
        public async Task<IActionResult> SaveActivityAsync([FromBody] PcActivityDto dto, CancellationToken ct) => Ok(await _schedule.SaveActivityAsync(dto, ct));

        [HttpDelete("activities/{id:int}")]
        public async Task<IActionResult> DeleteActivityAsync(int id, CancellationToken ct) => Ok(await _schedule.DeleteActivityAsync(id, ct));

        [HttpPost("activities/action")]
        public async Task<IActionResult> ActivityActionAsync([FromBody] PcActivityActionDto dto, CancellationToken ct) => Ok(await _schedule.ActivityActionAsync(dto, ct));

        [HttpGet("dependencies")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> DependenciesAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _schedule.GetDependenciesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("dependencies")]
        public async Task<IActionResult> SaveDependencyAsync([FromBody] PcDependencyDto dto, CancellationToken ct) => Ok(await _schedule.SaveDependencyAsync(dto, ct));

        [HttpDelete("dependencies/{id:int}")]
        public async Task<IActionResult> DeleteDependencyAsync(int id, CancellationToken ct) => Ok(await _schedule.DeleteDependencyAsync(id, ct));

        [HttpPost("cpm/run")]
        public async Task<IActionResult> RunCpmAsync([FromBody] PcCpmRunRequestDto dto, CancellationToken ct) => Ok(await _schedule.RunCpmAsync(dto, ct));

        [HttpGet("executive-plan")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ExecutivePlanAsync([FromQuery] PcExecutivePlanQuery query, CancellationToken ct) => Ok((await _schedule.GetExecutivePlanAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("export/msproject/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> MsProjectAsync(int contractId, CancellationToken ct)
        {
            var result = await _schedule.ExportMsProjectXmlAsync(contractId, ct);
            if (!result.Success || string.IsNullOrEmpty(result.Data)) return Ok(result);
            return Content(result.Data, "application/xml", System.Text.Encoding.UTF8);
        }

        [HttpGet("need-by")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> NeedByAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _schedule.GetNeedByAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("need-by/apply")]
        public async Task<IActionResult> ApplyNeedByAsync([FromBody] PcNeedByApplyDto dto, CancellationToken ct) => Ok(await _schedule.ApplyNeedByAsync(dto, ct));

        [HttpPost("itp/generate")]
        public async Task<IActionResult> GenerateItpAsync([FromBody] PcItpGenerateDto dto, CancellationToken ct) => Ok(await _schedule.GenerateItpActivitiesAsync(dto, ct));

        [HttpGet("notices")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> NoticesAsync([FromQuery] PcNoticeQuery query, CancellationToken ct) => Ok((await _schedule.GetNoticeDraftsAsync(query, ct)).ToPaginatedResultSafe());
    }

    /// <summary>Physical progress, earned value, cost entries, weekly snapshots and the daily report.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol")]
    public class ProjectControlProgressController : ControllerBase
    {
        private readonly IPcProgressService _progress;

        public ProjectControlProgressController(IPcProgressService progress) { _progress = progress; }

        [HttpGet("progress/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ProgressAsync(int contractId, CancellationToken ct) => Ok(await _progress.GetProgressAsync(contractId, ct));

        [HttpGet("manual-progress")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ManualAsync([FromQuery] PcManualQuery query, CancellationToken ct) => Ok((await _progress.GetManualAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("manual-progress")]
        public async Task<IActionResult> ProposeManualAsync([FromBody] PcManualProgressSaveDto dto, CancellationToken ct) => Ok(await _progress.ProposeManualAsync(dto, ct));

        [HttpPost("manual-progress/decide")]
        public async Task<IActionResult> DecideManualAsync([FromBody] PcManualDecisionDto dto, CancellationToken ct) => Ok(await _progress.DecideManualAsync(dto, ct));

        [HttpGet("evm/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> EvmAsync(int contractId, [FromQuery] DateTime? asOf, CancellationToken ct) => Ok(await _progress.GetEvmAsync(contractId, asOf, ct));

        [HttpGet("cash-flow/{contractId:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> CashFlowAsync(int contractId, CancellationToken ct) => Ok(await _progress.GetCashFlowAsync(contractId, ct));

        [HttpGet("costs")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> CostsAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _progress.GetCostsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("costs")]
        public async Task<IActionResult> AddCostAsync([FromBody] PcCostEntrySaveDto dto, CancellationToken ct) => Ok(await _progress.AddCostAsync(dto, ct));

        [HttpGet("snapshots")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> SnapshotsAsync([FromQuery] PcSnapshotQuery query, CancellationToken ct) => Ok((await _progress.GetSnapshotsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("snapshots/{id:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> SnapshotAsync(int id, CancellationToken ct) => Ok(await _progress.GetSnapshotAsync(id, ct));

        [HttpPost("snapshots")]
        public async Task<IActionResult> TakeSnapshotAsync([FromBody] PcSnapshotRequestDto dto, CancellationToken ct) => Ok(await _progress.TakeSnapshotAsync(dto, ct));

        [HttpPost("snapshots/run-due")]
        public async Task<IActionResult> RunDueAsync([FromBody] PcSnapshotRunDto dto, CancellationToken ct) => Ok(await _progress.RunDueSnapshotsAsync(dto, ct));

        [HttpGet("daily-report")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> DailyAsync([FromQuery] PcDailyQuery query, CancellationToken ct) => Ok(await _progress.GetDailyAsync(query, ct));

        [HttpPost("daily-report")]
        public async Task<IActionResult> SaveDailyAsync([FromBody] PcDailyReportDto dto, CancellationToken ct) => Ok(await _progress.SaveDailyAsync(dto, ct));

        [HttpPost("daily-report/{id:int}/confirm")]
        public async Task<IActionResult> ConfirmDailyAsync(int id, CancellationToken ct) => Ok(await _progress.ConfirmDailyAsync(id, ct));
    }

    /// <summary>Payment milestones, guarantees, delay penalties and variation orders.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol")]
    public class ProjectControlCommercialController : ControllerBase
    {
        private readonly IPcCommercialService _commercial;

        public ProjectControlCommercialController(IPcCommercialService commercial) { _commercial = commercial; }

        [HttpGet("milestones")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> MilestonesAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _commercial.GetMilestonesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("milestones")]
        public async Task<IActionResult> SaveMilestoneAsync([FromBody] PcMilestoneDto dto, CancellationToken ct) => Ok(await _commercial.SaveMilestoneAsync(dto, ct));

        [HttpDelete("milestones/{id:int}")]
        public async Task<IActionResult> DeleteMilestoneAsync(int id, CancellationToken ct) => Ok(await _commercial.DeleteMilestoneAsync(id, ct));

        [HttpPost("milestones/action")]
        public async Task<IActionResult> MilestoneActionAsync([FromBody] PcMilestoneActionDto dto, CancellationToken ct) => Ok(await _commercial.MilestoneActionAsync(dto, ct));

        [HttpPost("milestones/evaluate/{contractId:int}")]
        public async Task<IActionResult> EvaluateMilestonesAsync(int contractId, CancellationToken ct) => Ok(await _commercial.EvaluateMilestonesAsync(contractId, ct));

        [HttpGet("guarantees")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> GuaranteesAsync([FromQuery] PcGuaranteeQuery query, CancellationToken ct) => Ok((await _commercial.GetGuaranteesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("guarantees/parse")]
        public async Task<IActionResult> ParseGuaranteesAsync(CancellationToken ct) => Ok(await _commercial.ParseGuaranteesAsync(ct));

        [HttpPost("guarantees")]
        public async Task<IActionResult> SaveGuaranteeAsync([FromBody] PcGuaranteeDto dto, CancellationToken ct) => Ok(await _commercial.SaveGuaranteeAsync(dto, ct));

        [HttpPost("guarantees/events")]
        public async Task<IActionResult> AddGuaranteeEventAsync([FromBody] PcGuaranteeEventSaveDto dto, CancellationToken ct) => Ok(await _commercial.AddGuaranteeEventAsync(dto, ct));

        [HttpGet("guarantees/alerts")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> GuaranteeAlertsAsync([FromQuery] int? contractId, CancellationToken ct) => Ok(ResponseDtoHelper.Ok(await _commercial.GuaranteeAlertsAsync(contractId, ct)));

        [HttpPost("penalties/compute/{contractId:int}")]
        public async Task<IActionResult> ComputePenaltiesAsync(int contractId, CancellationToken ct) => Ok(await _commercial.ComputePenaltiesAsync(contractId, ct));

        [HttpGet("penalties")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> PenaltiesAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _commercial.GetPenaltiesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("penalties/decide")]
        public async Task<IActionResult> DecidePenaltyAsync([FromBody] PcPenaltyDecisionDto dto, CancellationToken ct) => Ok(await _commercial.DecidePenaltyAsync(dto, ct));

        [HttpGet("variations")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> VariationsAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _commercial.GetVariationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("variations")]
        public async Task<IActionResult> SaveVariationAsync([FromBody] PcVariationDto dto, CancellationToken ct) => Ok(await _commercial.SaveVariationAsync(dto, ct));

        [HttpPost("variations/action")]
        public async Task<IActionResult> VariationActionAsync([FromBody] PcVariationActionDto dto, CancellationToken ct) => Ok(await _commercial.VariationActionAsync(dto, ct));
    }

    /// <summary>MDR / MDL, templates, response codes, transmittals and final books.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol")]
    public class ProjectControlDocumentsController : ControllerBase
    {
        private readonly IPcMdrService _mdr;

        public ProjectControlDocumentsController(IPcMdrService mdr) { _mdr = mdr; }

        [HttpPost("mdr/draft")]
        public async Task<IActionResult> DraftMdrAsync([FromBody] PcMdrDraftDto dto, CancellationToken ct) => Ok(await _mdr.DraftMdrAsync(dto, ct));

        [HttpGet("mdr")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> DeliverablesAsync([FromQuery] PcDeliverableQuery query, CancellationToken ct) => Ok((await _mdr.GetDeliverablesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("mdr")]
        public async Task<IActionResult> SaveDeliverableAsync([FromBody] PcDeliverableDto dto, CancellationToken ct) => Ok(await _mdr.SaveDeliverableAsync(dto, ct));

        [HttpPost("mdr/action")]
        public async Task<IActionResult> DeliverableActionAsync([FromBody] PcDeliverableActionDto dto, CancellationToken ct) => Ok(await _mdr.DeliverableActionAsync(dto, ct));

        [HttpGet("mdr-templates")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> TemplatesAsync([FromQuery] PaginationParams query, CancellationToken ct) => Ok((await _mdr.GetTemplatesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("mdr-templates")]
        public async Task<IActionResult> SaveTemplateAsync([FromBody] PcMdrTemplateDto dto, CancellationToken ct) => Ok(await _mdr.SaveTemplateAsync(dto, ct));

        [HttpGet("response-codes")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ResponseCodesAsync(CancellationToken ct) => Ok(await _mdr.GetResponseCodesAsync(ct));

        [HttpPost("response-codes")]
        public async Task<IActionResult> SaveResponseCodeAsync([FromBody] PcResponseCodeDto dto, CancellationToken ct) => Ok(await _mdr.SaveResponseCodeAsync(dto, ct));

        [HttpGet("transmittals")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> TransmittalsAsync([FromQuery] PcTransmittalQuery query, CancellationToken ct) => Ok((await _mdr.GetTransmittalsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("transmittals/{id:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> TransmittalAsync(int id, CancellationToken ct) => Ok(await _mdr.GetTransmittalAsync(id, ct));

        [HttpPost("transmittals")]
        public async Task<IActionResult> SaveTransmittalAsync([FromBody] PcTransmittalDto dto, CancellationToken ct) => Ok(await _mdr.SaveTransmittalAsync(dto, ct));

        [HttpPost("transmittals/response")]
        public async Task<IActionResult> RecordResponseAsync([FromBody] PcTransmittalResponseDto dto, CancellationToken ct) => Ok(await _mdr.RecordResponseAsync(dto, ct));

        [HttpPost("final-books")]
        public async Task<IActionResult> GenerateFinalBookAsync([FromBody] PcFinalBookRequestDto dto, CancellationToken ct) => Ok(await _mdr.GenerateFinalBookAsync(dto, ct));

        [HttpGet("final-books")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> FinalBooksAsync([FromQuery] PcFinalBookQuery query, CancellationToken ct) => Ok((await _mdr.GetFinalBooksAsync(query, ct)).ToPaginatedResultSafe());
    }

    /// <summary>Baselines, change control and claims.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol")]
    public class ProjectControlChangeController : ControllerBase
    {
        private readonly IPcBaselineService _baseline;

        public ProjectControlChangeController(IPcBaselineService baseline) { _baseline = baseline; }

        [HttpGet("baselines")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> BaselinesAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _baseline.GetBaselinesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("baselines/{id:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> BaselineAsync(int id, CancellationToken ct) => Ok(await _baseline.GetBaselineAsync(id, ct));

        [HttpPost("baselines")]
        public async Task<IActionResult> CreateBaselineAsync([FromBody] PcBaselineCreateDto dto, CancellationToken ct) => Ok(await _baseline.CreateBaselineAsync(dto, ct));

        [HttpPost("baselines/action")]
        public async Task<IActionResult> BaselineActionAsync([FromBody] PcBaselineActionDto dto, CancellationToken ct) => Ok(await _baseline.BaselineActionAsync(dto, ct));

        [HttpGet("cost-lines")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> CostLinesAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _baseline.GetCostLinesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("cost-lines")]
        public async Task<IActionResult> SaveCostLineAsync([FromBody] PcCostLineDto dto, CancellationToken ct) => Ok(await _baseline.SaveCostLineAsync(dto, ct));

        [HttpDelete("cost-lines/{id:int}")]
        public async Task<IActionResult> DeleteCostLineAsync(int id, CancellationToken ct) => Ok(await _baseline.DeleteCostLineAsync(id, ct));

        [HttpGet("changes")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ChangesAsync([FromQuery] PcChangeQuery query, CancellationToken ct) => Ok((await _baseline.GetChangesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("changes/{id:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ChangeAsync(int id, CancellationToken ct) => Ok(await _baseline.GetChangeAsync(id, ct));

        [HttpPost("changes")]
        public async Task<IActionResult> SaveChangeAsync([FromBody] PcChangeDto dto, CancellationToken ct) => Ok(await _baseline.SaveChangeAsync(dto, ct));

        [HttpPost("changes/action")]
        public async Task<IActionResult> ChangeActionAsync([FromBody] PcChangeActionDto dto, CancellationToken ct) => Ok(await _baseline.ChangeActionAsync(dto, ct));

        [HttpGet("approval-authorities")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> AuthoritiesAsync(CancellationToken ct) => Ok(await _baseline.GetAuthoritiesAsync(ct));

        [HttpPost("approval-authorities")]
        public async Task<IActionResult> SaveAuthorityAsync([FromBody] PcAuthorityDto dto, CancellationToken ct) => Ok(await _baseline.SaveAuthorityAsync(dto, ct));

        [HttpGet("claims")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ClaimsAsync([FromQuery] PcContractQuery query, CancellationToken ct) => Ok((await _baseline.GetClaimsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("claims")]
        public async Task<IActionResult> SaveClaimAsync([FromBody] PcClaimDto dto, CancellationToken ct) => Ok(await _baseline.SaveClaimAsync(dto, ct));

        [HttpPost("claims/action")]
        public async Task<IActionResult> ClaimActionAsync([FromBody] PcClaimActionDto dto, CancellationToken ct) => Ok(await _baseline.ClaimActionAsync(dto, ct));
    }

    /// <summary>Shipments (shipping gate), periodic reports and the portfolio for the CEO dashboard.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/projectcontrol")]
    public class ProjectControlReportController : ControllerBase
    {
        private readonly IPcReportService _report;

        public ProjectControlReportController(IPcReportService report) { _report = report; }

        [HttpGet("shipments")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ShipmentsAsync([FromQuery] PcShipmentQuery query, CancellationToken ct) => Ok((await _report.GetShipmentsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("shipments")]
        public async Task<IActionResult> SaveShipmentAsync([FromBody] PcShipmentDto dto, CancellationToken ct) => Ok(await _report.SaveShipmentAsync(dto, ct));

        [HttpPost("shipments/action")]
        public async Task<IActionResult> ShipmentActionAsync([FromBody] PcShipmentActionDto dto, CancellationToken ct) => Ok(await _report.ShipmentActionAsync(dto, ct));

        [HttpGet("reports")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ReportsAsync([FromQuery] PcReportQuery query, CancellationToken ct) => Ok((await _report.GetReportsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("reports/{id:int}")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> ReportAsync(int id, CancellationToken ct) => Ok(await _report.GetReportAsync(id, ct));

        [HttpPost("reports/generate")]
        public async Task<IActionResult> GenerateReportAsync([FromBody] PcReportRequestDto dto, CancellationToken ct) => Ok(await _report.GenerateReportAsync(dto, ct));

        [HttpPost("reports/{id:int}/release")]
        public async Task<IActionResult> ReleaseReportAsync(int id, CancellationToken ct) => Ok(await _report.ReleaseReportAsync(id, ct));

        [HttpGet("portfolio")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> PortfolioAsync([FromQuery] PaginationParams query, CancellationToken ct) => Ok((await _report.GetPortfolioAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("dashboard")]
        [Authorize(Roles = ProjectControlRoles.Readers)]
        public async Task<IActionResult> DashboardAsync(CancellationToken ct) => Ok(ResponseDtoHelper.Ok(await _report.GetDashboardAsync(ct)));
    }
}
