using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.EngInbox;
using ViewModels.NasbNiro.EngInboxDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // ENG-F1: the engineering inbox. The engineering unit drops a package (a workbook and its document files) into a folder on the server; the engineering agent reads it. This controller shows what arrived, what was refused
    // and why, what the agent did with it, starts a scan on demand and lets the engineering unit dismiss a package with a reason. The services check the roles again and refuse an AI seat where it would decide.
    [ApiController]
    [Authorize(Roles = EngInboxRoles.Readers)]
    [Route("api/v1/engineering/inbox")]
    public class EngInboxController : ControllerBase
    {
        private readonly IEngInboxService _service;

        public EngInboxController(IEngInboxService service) { _service = service; }

        /// <summary>The source, its folder, the last scan and the number of packages per status.</summary>
        [HttpGet("status")]
        public async Task<IActionResult> StatusAsync(CancellationToken ct) => Ok(await _service.GetStatusAsync(ct));

        [HttpGet("packages")]
        public async Task<IActionResult> PackagesAsync([FromQuery] EngInboxQuery query, CancellationToken ct) => Ok((await _service.GetPackagesAsync(query, ct)).ToPaginatedResultSafe());

        /// <summary>One package: information, findings, BOM, OPC, document references and MDL lines.</summary>
        [HttpGet("packages/{id:int}")]
        public async Task<IActionResult> PackageAsync(int id, CancellationToken ct) => Ok(await _service.GetPackageAsync(id, ct));

        /// <summary>Scan the source now (the agent does it every few minutes anyway).</summary>
        [HttpPost("scan")]
        [Authorize(Roles = EngInboxRoles.Operators)]
        public async Task<IActionResult> ScanAsync(CancellationToken ct) => Ok(await _service.ScanAsync(ct));

        /// <summary>Close a rejected, blocked or waiting package with a reason of at least 15 characters.</summary>
        [HttpPost("packages/{id:int}/dismiss")]
        [Authorize(Roles = EngInboxRoles.Operators)]
        public async Task<IActionResult> DismissAsync(int id, [FromBody] EngInboxDismissDto dto, CancellationToken ct)
        {
            if (dto != null) dto.Id = id;
            return Ok(await _service.DismissAsync(dto, ct));
        }

        /// <summary>The empty template workbook.</summary>
        [HttpGet("template")]
        public IActionResult Template() => File(EngPackageWriter.Template(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ENG-PACKAGE-template.xlsx");

        /// <summary>The complete fictional sample package (a zip of one package folder).</summary>
        [HttpGet("sample")]
        public IActionResult Sample() => File(EngPackageWriter.SampleZip(), "application/zip", "ENG-PACKAGE-sample-TEST-PLT-01.zip");
    }
}
