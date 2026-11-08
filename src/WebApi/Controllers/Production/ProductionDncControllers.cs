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
    // Production release P2 part A: DNC. Role checks per action (segregation of duties) are in the service; rule violations answer 200 with success=false.

    /// <summary>Machine links, the CNC program library, revisions, transfers, runs and the terminal view of an operation's program.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/production/dnc")]
    [RolesCheckedInService]
    public class ProductionDncController : ControllerBase
    {
        private const string PlainText = "application/octet-stream";
        private readonly IMfDncService _dnc;

        public ProductionDncController(IMfDncService dnc)
        {
            _dnc = dnc;
        }

        private IActionResult FileOrError(ResponseDto<NcFileDto> r)
        {
            if (!r.Success || r.Data?.Bytes == null) return Ok(r);
            if (!string.IsNullOrEmpty(r.Data.Sha256)) Response.Headers["X-Content-Sha256"] = r.Data.Sha256;
            return File(r.Data.Bytes, PlainText, r.Data.FileName);
        }

        // ------------------------------------------------------------------ machine links

        [HttpGet("connections")]
        public async Task<IActionResult> ConnectionsAsync([FromQuery] MachineConnectionQuery query, CancellationToken ct) => Ok((await _dnc.GetConnectionsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("connections/{machineId:int}")]
        public async Task<IActionResult> ConnectionAsync(int machineId, CancellationToken ct) => Ok(await _dnc.GetConnectionAsync(machineId, ct));

        [HttpPost("connections")]
        public async Task<IActionResult> SaveConnectionAsync([FromBody] SaveMachineConnectionDto dto, CancellationToken ct) => Ok(await _dnc.SaveConnectionAsync(dto, ct));

        [HttpPost("connections/gateway-key")]
        public async Task<IActionResult> GatewayKeyAsync([FromBody] MachineIdDto dto, CancellationToken ct) => Ok(await _dnc.NewGatewayKeyAsync(dto, ct));

        [HttpPost("connections/test")]
        public async Task<IActionResult> TestConnectionAsync([FromBody] MachineIdDto dto, CancellationToken ct) => Ok(await _dnc.TestConnectionAsync(dto, ct));

        // ------------------------------------------------------------------ programs and revisions

        [HttpGet("programs")]
        public async Task<IActionResult> ProgramsAsync([FromQuery] NcProgramQuery query, CancellationToken ct) => Ok((await _dnc.GetProgramsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("programs/{id:int}")]
        public async Task<IActionResult> ProgramAsync(int id, CancellationToken ct) => Ok(await _dnc.GetProgramAsync(id, ct));

        [HttpPost("programs")]
        public async Task<IActionResult> SaveProgramAsync([FromBody] SaveNcProgramDto dto, CancellationToken ct) => Ok(await _dnc.SaveProgramAsync(dto, ct));

        [HttpPost("revisions")]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> UploadRevisionAsync([FromBody] UploadNcRevisionDto dto, CancellationToken ct) => Ok(await _dnc.UploadRevisionAsync(dto, ct));

        [HttpPost("revisions/action")]
        public async Task<IActionResult> RevisionActionAsync([FromBody] NcRevisionActionDto dto, CancellationToken ct) => Ok(await _dnc.RevisionActionAsync(dto, ct));

        [HttpPost("revisions/validate")]
        public async Task<IActionResult> ValidateAsync([FromBody] ValidateNcDto dto, CancellationToken ct) => Ok(await _dnc.ValidateAsync(dto, ct));

        [HttpGet("revisions/{id:int}/content")]
        public async Task<IActionResult> RevisionContentAsync(int id, CancellationToken ct) => FileOrError(await _dnc.GetRevisionContentAsync(id, ct));

        [HttpGet("revisions/{id:int}/diff")]
        public async Task<IActionResult> RevisionDiffAsync(int id, CancellationToken ct) => Ok(await _dnc.GetRevisionDiffAsync(id, ct));

        // ------------------------------------------------------------------ transfers, runs, terminal

        [HttpGet("transfers")]
        public async Task<IActionResult> TransfersAsync([FromQuery] NcTransferQuery query, CancellationToken ct) => Ok((await _dnc.GetTransfersAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("transfers/send")]
        public async Task<IActionResult> SendAsync([FromBody] SendNcDto dto, CancellationToken ct) => Ok(await _dnc.SendAsync(dto, ct));

        [HttpPost("transfers/receive")]
        public async Task<IActionResult> ReceiveAsync([FromBody] ReceiveNcDto dto, CancellationToken ct) => Ok(await _dnc.ReceiveAsync(dto, ct));

        [HttpPost("transfers/action")]
        public async Task<IActionResult> TransferActionAsync([FromBody] NcTransferActionDto dto, CancellationToken ct) => Ok(await _dnc.TransferActionAsync(dto, ct));

        /// <summary>The file of a manual transfer (USB stick): the download is the delivery.</summary>
        [HttpGet("transfers/{id:int}/file")]
        public async Task<IActionResult> TransferFileAsync(int id, CancellationToken ct) => FileOrError(await _dnc.GetTransferFileAsync(id, ct));

        [HttpPost("transfers/run-pending")]
        public async Task<IActionResult> RunPendingAsync(CancellationToken ct) => Ok(await _dnc.RunPendingAsync(ct));

        [HttpGet("runs")]
        public async Task<IActionResult> RunsAsync([FromQuery] NcRunQuery query, CancellationToken ct) => Ok((await _dnc.GetRunsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("terminal")]
        public async Task<IActionResult> TerminalAsync([FromQuery] TerminalNcQuery query, CancellationToken ct) => Ok(await _dnc.GetTerminalAsync(query, ct));
    }

    /// <summary>
    /// For an edge box at a machine (pull: the server never connects to it). Headers X-Dnc-Machine (machine id) and X-Dnc-Key; a wrong machine, key or (for a gateway bound to addresses) source address gives 401.
    /// </summary>
    [ApiController]
    [Route("api/v1/production/dnc/gateway")]
    public class ProductionDncGatewayController : ControllerBase
    {
        private readonly IMfDncService _dnc;

        public ProductionDncGatewayController(IMfDncService dnc)
        {
            _dnc = dnc;
        }

        private string Machine => Request.Headers["X-Dnc-Machine"].ToString();
        private string Key => Request.Headers["X-Dnc-Key"].ToString();
        private string Remote => HttpContext.Connection.RemoteIpAddress?.ToString();

        private IActionResult Answer<T>(ResponseDto<T> r) => r.HttpStatusCode == HttpStatusCode.Unauthorized ? StatusCode(401, r) : Ok(r);

        [HttpGet("jobs")]
        [AllowAnonymous]
        public async Task<IActionResult> JobsAsync(CancellationToken ct) => Answer(await _dnc.GatewayJobsAsync(Machine, Key, Remote, ct));

        [HttpGet("jobs/{id:int}/content")]
        [AllowAnonymous]
        public async Task<IActionResult> ContentAsync(int id, CancellationToken ct)
        {
            ResponseDto<NcFileDto> r = await _dnc.GatewayContentAsync(Machine, Key, Remote, id, ct);
            if (!r.Success || r.Data?.Bytes == null) return Answer(r);
            Response.Headers["X-Content-Sha256"] = r.Data.Sha256;
            return File(r.Data.Bytes, "application/octet-stream", r.Data.FileName);
        }

        [HttpPost("jobs/result")]
        [AllowAnonymous]
        public async Task<IActionResult> ResultAsync([FromBody] GatewayResultDto dto, CancellationToken ct) => Answer(await _dnc.GatewayResultAsync(Machine, Key, Remote, dto, ct));

        [HttpPost("upload")]
        [AllowAnonymous]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> UploadAsync([FromBody] GatewayUploadDto dto, CancellationToken ct) => Answer(await _dnc.GatewayUploadAsync(Machine, Key, Remote, dto, ct));
    }
}
