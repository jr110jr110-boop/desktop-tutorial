using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Production.Dnc;
using ViewModels.NasbNiro.ProductionDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Production release P2 part E: the guarded machining process. Role checks per action are in the services; rule violations answer 200 with success=false.

    /// <summary>The data link to each control, the start interlock, the time line of what happened on the machine, the terminal's gate panel and the production manager's one-time overrides.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production/guard")]
    [RolesCheckedInService]
    public class ProductionGuardController : ControllerBase
    {
        private readonly IMfGuardService _guard;

        public ProductionGuardController(IMfGuardService guard)
        {
            _guard = guard;
        }

        [HttpGet("machines")]
        public async Task<IActionResult> MachinesAsync([FromQuery] int? workCenterId, CancellationToken ct) => Ok(await _guard.GetMachinesAsync(workCenterId, ct));

        [HttpPost("machines")]
        public async Task<IActionResult> SaveAsync([FromBody] SaveMachineGuardDto dto, CancellationToken ct) => Ok(await _guard.SaveAsync(dto, ct));

        [HttpPost("machines/test")]
        public async Task<IActionResult> TestAsync([FromBody] MachineIdDto dto, CancellationToken ct) => Ok(await _guard.TestAsync(dto, ct));

        [HttpPost("poll")]
        public async Task<IActionResult> PollAsync(CancellationToken ct) => Ok(await _guard.RunPollAsync(ct));

        [HttpGet("activity")]
        public async Task<IActionResult> ActivityAsync([FromQuery] MachineActivityQuery query, CancellationToken ct) => Ok((await _guard.GetActivityAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("terminal")]
        public async Task<IActionResult> TerminalAsync([FromQuery] int operationId, [FromQuery] int machineId, CancellationToken ct) => Ok(await _guard.TerminalAsync(operationId, machineId, ct));

        [HttpGet("overrides")]
        public async Task<IActionResult> OverridesAsync([FromQuery] GateOverrideQuery query, CancellationToken ct) => Ok((await _guard.GetOverridesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("overrides")]
        public async Task<IActionResult> CreateOverrideAsync([FromBody] CreateGateOverrideDto dto, CancellationToken ct) => Ok(await _guard.CreateOverrideAsync(dto, ct));
    }

    /// <summary>Post processors (two-person approval) and the CAM evidence of a program revision: post processor, simulation report, CAM tool list.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production/dnc/cam")]
    [RolesCheckedInService]
    public class ProductionCamEvidenceController : ControllerBase
    {
        private readonly IMfCamEvidenceService _cam;

        public ProductionCamEvidenceController(IMfCamEvidenceService cam)
        {
            _cam = cam;
        }

        [HttpGet("posts")]
        public async Task<IActionResult> PostsAsync([FromQuery] PostProcessorQuery query, CancellationToken ct) => Ok((await _cam.GetPostsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("posts")]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> CreatePostAsync([FromBody] SavePostProcessorDto dto, CancellationToken ct) => Ok(await _cam.CreatePostAsync(dto, ct));

        [HttpPost("posts/action")]
        public async Task<IActionResult> PostActionAsync([FromBody] PostProcessorActionDto dto, CancellationToken ct) => Ok(await _cam.PostActionAsync(dto, ct));

        [HttpGet("posts/{id:int}/file")]
        public async Task<IActionResult> PostFileAsync(int id, CancellationToken ct) => Ok(await _cam.GetPostFileAsync(id, ct));

        [HttpGet("revisions/{revisionId:int}")]
        public async Task<IActionResult> RevisionAsync(int revisionId, CancellationToken ct) => Ok(await _cam.GetRevisionAsync(revisionId, ct));

        [HttpPost("revisions")]
        public async Task<IActionResult> SetRevisionAsync([FromBody] SetRevisionCamDto dto, CancellationToken ct) => Ok(await _cam.SetRevisionAsync(dto, ct));

        [HttpPost("revisions/attachments")]
        [RequestSizeLimit(32 * 1024 * 1024)]
        public async Task<IActionResult> UploadAsync([FromBody] UploadNcAttachmentDto dto, CancellationToken ct) => Ok(await _cam.UploadAttachmentAsync(dto, ct));

        [HttpPost("revisions/tools")]
        [RequestSizeLimit(1024 * 1024)]
        public async Task<IActionResult> ToolsAsync([FromBody] ImportCamToolsDto dto, CancellationToken ct) => Ok(await _cam.ImportToolsAsync(dto, ct));

        [HttpGet("attachments/{id:int}")]
        public async Task<IActionResult> AttachmentAsync(int id, CancellationToken ct) => Ok(await _cam.GetAttachmentAsync(id, ct));
    }

    /// <summary>Measuring stations (digital gauges) and the measurements of an operation or a machine.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production/measure")]
    [RolesCheckedInService]
    public class ProductionMeasureController : ControllerBase
    {
        private readonly IMfMeasureService _measure;

        public ProductionMeasureController(IMfMeasureService measure)
        {
            _measure = measure;
        }

        [HttpGet("stations")]
        public async Task<IActionResult> StationsAsync(CancellationToken ct) => Ok(await _measure.GetStationsAsync(ct));

        [HttpPost("stations")]
        public async Task<IActionResult> SaveStationAsync([FromBody] MeasureStationDto dto, CancellationToken ct) => Ok(await _measure.SaveStationAsync(dto, ct));

        [HttpPost("stations/{id:int}/key")]
        public async Task<IActionResult> KeyAsync(int id, CancellationToken ct) => Ok(await _measure.IssueKeyAsync(id, ct));

        [HttpGet("values")]
        public async Task<IActionResult> ValuesAsync([FromQuery] MeasurementQuery query, CancellationToken ct) => Ok((await _measure.GetMeasurementsAsync(query, ct)).ToPaginatedResultSafe());
    }

    /// <summary>For a gauge interface (a box or a PC with the gauges' receiver). Headers X-Measure-Station (station code) and X-Measure-Key; a wrong station, key or source address gives 401.</summary>
    [ApiController]
    [Route("api/v1/production/measure/device")]
    public class ProductionMeasureDeviceController : ControllerBase
    {
        private readonly IMfMeasureService _measure;

        public ProductionMeasureDeviceController(IMfMeasureService measure)
        {
            _measure = measure;
        }

        [HttpPost("push")]
        [AllowAnonymous]
        [RequestSizeLimit(256 * 1024)]
        public async Task<IActionResult> PushAsync([FromBody] MeasurementPushDto dto, CancellationToken ct)
        {
            ResponseDto<MeasurementPushResultDto> r = await _measure.PushAsync(Request.Headers["X-Measure-Station"].ToString(), Request.Headers["X-Measure-Key"].ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString(), dto, ct);
            return r.HttpStatusCode == HttpStatusCode.Unauthorized ? StatusCode(401, r) : Ok(r);
        }
    }
}
