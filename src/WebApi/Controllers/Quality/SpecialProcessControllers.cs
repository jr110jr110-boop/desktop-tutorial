using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Quality;
using ViewModels.NasbNiro.QualityDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // Package C (special processes and material verification). The role of each step (maker, checker, QA) is checked in the services; reads are open to the quality readers.

    /// <summary>Special processes: the process types, the qualifications, the run records and the supplier approvals.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality/special-processes")]
    [RolesCheckedInService]
    public class QualitySpecialProcessController : ControllerBase
    {
        private readonly ISpecialProcessService _service;

        public QualitySpecialProcessController(ISpecialProcessService service) => _service = service;

        [HttpGet("types")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> TypesAsync([FromQuery] SpTypeQuery query, CancellationToken ct) => Ok(await _service.GetTypesAsync(query, ct));

        [HttpPost("types")]
        public async Task<IActionResult> SaveTypeAsync([FromBody] SaveSpTypeDto dto, CancellationToken ct) => Ok(await _service.SaveTypeAsync(dto, ct));

        [HttpPost("types/change-action")]
        public async Task<IActionResult> TypeChangeActionAsync([FromBody] SpActionDto dto, CancellationToken ct) => Ok(await _service.TypeChangeActionAsync(dto, ct));

        [HttpGet("qualifications")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> QualificationsAsync([FromQuery] SpQualQuery query, CancellationToken ct) => Ok((await _service.GetQualificationsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("qualifications/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> QualificationAsync(int id, CancellationToken ct) => Ok(await _service.GetQualificationAsync(id, ct));

        [HttpPost("qualifications")]
        public async Task<IActionResult> SaveQualificationAsync([FromBody] SaveSpQualDto dto, CancellationToken ct) => Ok(await _service.SaveQualificationAsync(dto, ct));

        [HttpPost("qualifications/action")]
        public async Task<IActionResult> QualificationActionAsync([FromBody] SpActionDto dto, CancellationToken ct) => Ok(await _service.QualificationActionAsync(dto, ct));

        [HttpGet("runs")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> RunsAsync([FromQuery] SpRunQuery query, CancellationToken ct) => Ok((await _service.GetRunsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("runs/{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> RunAsync(int id, CancellationToken ct) => Ok(await _service.GetRunAsync(id, ct));

        [HttpPost("runs")]
        public async Task<IActionResult> SaveRunAsync([FromBody] SaveSpRunDto dto, CancellationToken ct) => Ok(await _service.SaveRunAsync(dto, ct));

        [HttpPost("runs/action")]
        public async Task<IActionResult> RunActionAsync([FromBody] SpActionDto dto, CancellationToken ct) => Ok(await _service.RunActionAsync(dto, ct));

        /// <summary>The state of the finish gate of a step (what is missing before it can be finished).</summary>
        [HttpGet("gate")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> GateAsync([FromQuery] int? operationId, [FromQuery] int? partOperationId, CancellationToken ct) => Ok(await _service.GateStatusAsync(operationId, partOperationId, ct));

        [HttpGet("supplier-approvals")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> SuppliersAsync([FromQuery] SpSupplierQuery query, CancellationToken ct) => Ok((await _service.GetSupplierApprovalsAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("supplier-approvals")]
        public async Task<IActionResult> SaveSupplierAsync([FromBody] SaveSpSupplierApprovalDto dto, CancellationToken ct) => Ok(await _service.SaveSupplierApprovalAsync(dto, ct));

        [HttpPost("supplier-approvals/action")]
        public async Task<IActionResult> SupplierActionAsync([FromBody] SpActionDto dto, CancellationToken ct) => Ok(await _service.SupplierApprovalActionAsync(dto, ct));
    }

    /// <summary>Material specifications (grade + standard) and the structured EN 10204 certificate.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/quality/material-specs")]
    [RolesCheckedInService]
    public class QualityMaterialSpecController : ControllerBase
    {
        private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private readonly IMaterialSpecService _service;

        public QualityMaterialSpecController(IMaterialSpecService service) => _service = service;

        [HttpGet]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> ListAsync([FromQuery] MaterialSpecQuery query, CancellationToken ct) => Ok((await _service.GetAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("{id:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> OneAsync(int id, CancellationToken ct) => Ok(await _service.GetOneAsync(id, ct));

        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromBody] SaveMaterialSpecDto dto, CancellationToken ct) => Ok(await _service.SaveAsync(dto, ct));

        [HttpPost("action")]
        public async Task<IActionResult> ActionAsync([FromBody] SpActionDto dto, CancellationToken ct) => Ok(await _service.ActionAsync(dto, ct));

        /// <summary>The certificate type a coded item needs (derived from the specification and the BOM line).</summary>
        [HttpGet("required/{itemId:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> RequiredAsync(int itemId, CancellationToken ct) => Ok(await _service.RequiredAsync(itemId, ct));

        /// <summary>The data of the certificate verification sheet (printable).</summary>
        [HttpGet("certificate-sheet/{certificateId:int}")]
        [Authorize(Roles = QualityRoles.Readers)]
        public async Task<IActionResult> SheetAsync(int certificateId, CancellationToken ct) => Ok(await _service.SheetAsync(certificateId, ct));

        [HttpGet("certificate-template")]
        [Authorize(Roles = QualityRoles.CertificateWriters)]
        public IActionResult Template()
        {
            var result = _service.CertificateTemplate();
            if (!result.Success || result.Data == null) return BadRequest(result);
            return File(result.Data, Xlsx, "certificate-values-template.xlsx");
        }

        /// <summary>Preview (Preview=true) or write (Preview=false) the chemistry and mechanical values of a pending certificate; the verdict is computed by the server.</summary>
        [HttpPost("certificate-import")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> ImportAsync([FromForm] CertExcelRequestDto request, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0) return Ok(Common.Results.ResponseDtoHelper.BadRequest<CertExcelResultDto>("فایل را انتخاب کنید."));
            using var ms = new System.IO.MemoryStream();
            await file.CopyToAsync(ms, ct);
            return Ok(await _service.ImportValuesAsync(request, ms.ToArray(), ct));
        }
    }
}
