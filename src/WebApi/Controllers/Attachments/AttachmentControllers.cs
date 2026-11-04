using System.Threading;
using System.Threading.Tasks;
using Entities.Model.NasbNiro.Attachments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Attachments;
using ViewModels.NasbNiro.AttachmentDtos;
using WebApi.Filters;

namespace WebApi.Controllers
{
    // PB3: files attached to a record, through the whole chain. The service decides read and write roles per owner record (the owning module's lists);
    // a file is never deleted: before approval the uploader removes (logged), afterwards it is voided with a reason or superseded.
    [ApiController]
    [Authorize]
    [RolesCheckedInService]
    [Route("api/v1/attachments")]
    public class AttachmentsController : ControllerBase
    {
        /// <summary>The hard ceiling of one request; the real cap per file is the setting AttachmentMaxMegabytes (default 50), enforced by the service.</summary>
        private const long RequestCeiling = 200L * 1024 * 1024;

        private readonly IAttachmentService _service;
        public AttachmentsController(IAttachmentService service) => _service = service;

        [HttpGet]
        public async Task<IActionResult> PanelAsync([FromQuery] AttachmentOwnerType ownerType, [FromQuery] int ownerId, CancellationToken ct) => Ok(await _service.GetPanelAsync(ownerType, ownerId, ct));

        [HttpPost]
        [RequestSizeLimit(RequestCeiling)]
        [RequestFormLimits(MultipartBodyLengthLimit = RequestCeiling)]
        public async Task<IActionResult> UploadAsync([FromForm] UploadAttachmentForm form, CancellationToken ct) => Ok(await _service.UploadAsync(form, ct));

        [HttpPost("void")]
        public async Task<IActionResult> VoidAsync([FromBody] VoidAttachmentDto dto, CancellationToken ct) => Ok(await _service.VoidAsync(dto, ct));

        /// <summary>The uploader takes his file away before the owner is approved (hidden, logged; the file stays). After approval: refused, use void.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> RemoveAsync(int id, CancellationToken ct) => Ok(await _service.RemoveAsync(id, ct));
    }

    // PB3: پرونده درخواست
    [ApiController]
    [Authorize]
    [RolesCheckedInService]
    [Route("api/v1/dossier")]
    public class DossierController : ControllerBase
    {
        private readonly IDossierService _service;
        public DossierController(IDossierService service) => _service = service;

        /// <summary>The whole chain of a request by any of its numbers (request, inquiry, order, receipt, inspection, NCR, invoice, payment, petty cash). <c>kind</c> picks one of several matches, or SupplierInvoiceNumber to search the supplier's own invoice number.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAsync([FromQuery] string number, [FromQuery] string kind, CancellationToken ct) => Ok(await _service.GetAsync(number, ct, kind));
    }
}
