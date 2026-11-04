using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.ProjectControl;
using ViewModels.NasbNiro.ProjectControlDtos;
using WebApi.Extensions;
using WebApi.Filters;

namespace WebApi.Controllers.ProjectControl
{
    /// <summary>Engineering document control: revisions, check-out, links, tags, distribution, approver matrix, notices, shop-floor viewer, Excel and the tender register. Roles are checked in the service.</summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/edms")]
    [RolesCheckedInService]
    public class EdmsController : ControllerBase
    {
        private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        private readonly IEdmsService _edms;

        public EdmsController(IEdmsService edms) { _edms = edms; }

        // revisions
        [HttpGet("deliverables/{id:int}/history")]
        public async Task<IActionResult> HistoryAsync(int id, CancellationToken ct) => Ok(await _edms.GetHistoryAsync(id, ct));

        [HttpGet("revisions/{id:int}")]
        public async Task<IActionResult> RevisionAsync(int id, CancellationToken ct) => Ok(await _edms.GetRevisionAsync(id, ct));

        [HttpGet("revisions/by")]
        public async Task<IActionResult> RevisionsOfAsync([FromQuery] string kind, [FromQuery] int id, CancellationToken ct) => Ok(await _edms.RevisionsOfAsync(kind, id, ct));

        [HttpGet("revisions/compare")]
        public async Task<IActionResult> CompareAsync([FromQuery] int a, [FromQuery] int b, CancellationToken ct) => Ok(await _edms.CompareAsync(a, b, ct));

        [HttpPost("revisions")]
        public async Task<IActionResult> CreateRevisionAsync([FromBody] PcRevisionCreateDto dto, CancellationToken ct) => Ok(await _edms.CreateRevisionAsync(dto, ct));

        [HttpPost("revisions/files")]
        public async Task<IActionResult> AddFileAsync([FromBody] PcRevisionFileAddDto dto, CancellationToken ct) => Ok(await _edms.AddFileAsync(dto, ct));

        [HttpPost("revisions/files/remove")]
        public async Task<IActionResult> RemoveFileAsync([FromBody] PcRevisionFileRemoveDto dto, CancellationToken ct) => Ok(await _edms.RemoveFileAsync(dto, ct));

        [HttpPost("revisions/action")]
        public async Task<IActionResult> ActionAsync([FromBody] PcRevisionActionDto dto, CancellationToken ct) => Ok(await _edms.ActionAsync(dto, ct));

        // check-out
        [HttpPost("checkout")]
        public async Task<IActionResult> CheckOutAsync([FromBody] PcCheckOutDto dto, CancellationToken ct) => Ok(await _edms.CheckOutAsync(dto, ct));

        [HttpPost("checkin")]
        public async Task<IActionResult> CheckInAsync([FromBody] PcCheckOutDto dto, CancellationToken ct) => Ok(await _edms.CheckInAsync(dto, ct));

        [HttpPost("checkout/cancel")]
        public async Task<IActionResult> CancelCheckOutAsync([FromBody] PcCheckOutDto dto, CancellationToken ct) => Ok(await _edms.CancelCheckOutAsync(dto, ct));

        // links and tags
        [HttpGet("links")]
        public async Task<IActionResult> LinksAsync([FromQuery] PcDocLinkQuery query, CancellationToken ct) => Ok(await _edms.GetLinksAsync(query, ct));

        [HttpPost("links")]
        public async Task<IActionResult> SaveLinkAsync([FromBody] PcDocLinkSaveDto dto, CancellationToken ct) => Ok(await _edms.SaveLinkAsync(dto, ct));

        [HttpPost("links/void")]
        public async Task<IActionResult> VoidLinkAsync([FromBody] PcDocLinkVoidDto dto, CancellationToken ct) => Ok(await _edms.VoidLinkAsync(dto, ct));

        [HttpGet("part-documents")]
        public async Task<IActionResult> PartDocumentsAsync([FromQuery] PcDocLinkQuery query, CancellationToken ct) => Ok(await _edms.GetPartDocumentsAsync(query, ct));

        [HttpGet("tags")]
        public async Task<IActionResult> TagsAsync([FromQuery] int contractId, CancellationToken ct) => Ok(await _edms.GetTagsAsync(contractId, ct));

        [HttpPost("tags")]
        public async Task<IActionResult> SaveTagAsync([FromBody] PcTagSaveDto dto, CancellationToken ct) => Ok(await _edms.SaveTagAsync(dto, ct));

        // distribution, matrix, notices
        [HttpGet("distribution")]
        public async Task<IActionResult> DistributionAsync(CancellationToken ct) => Ok(await _edms.GetDistributionAsync(ct));

        [HttpPost("distribution")]
        public async Task<IActionResult> SaveDistributionAsync([FromBody] PcDistributionSaveDto dto, CancellationToken ct) => Ok(await _edms.SaveDistributionAsync(dto, ct));

        [HttpGet("approvers")]
        public async Task<IActionResult> ApproversAsync(CancellationToken ct) => Ok(await _edms.GetApproversAsync(ct));

        [HttpPost("approvers")]
        public async Task<IActionResult> SaveApproverAsync([FromBody] PcApproverSaveDto dto, CancellationToken ct) => Ok(await _edms.SaveApproverAsync(dto, ct));

        [HttpGet("notices")]
        public async Task<IActionResult> NoticesAsync([FromQuery] PcDocNoticeQuery query, CancellationToken ct) => Ok((await _edms.GetNoticesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpPost("notices/{id:int}/acknowledge")]
        public async Task<IActionResult> AcknowledgeAsync(int id, CancellationToken ct) => Ok(await _edms.AcknowledgeAsync(id, ct));

        // shop floor
        [HttpGet("shop/orders/{orderId:int}/documents")]
        public async Task<IActionResult> ShopDocumentsAsync(int orderId, [FromQuery] int? operationId, CancellationToken ct) => Ok(await _edms.GetShopDocumentsAsync(orderId, operationId, ct));

        [HttpGet("controlled-copy")]
        public async Task<IActionResult> ControlledCopyAsync([FromQuery] int revisionId, [FromQuery] Guid? documentId, [FromQuery] bool print, CancellationToken ct) =>
            Ok(await _edms.ControlledCopyAsync("revision", revisionId, documentId, print, ct));

        // Excel
        [HttpGet("mdr/export")]
        public async Task<IActionResult> ExportAsync([FromQuery] int contractId, [FromQuery] bool clientOnly, [FromQuery] int? workOrderId, [FromQuery] bool template, CancellationToken ct)
        {
            ResponseDto<byte[]> r = await _edms.ExportMdrAsync(contractId, clientOnly, workOrderId, template, ct);
            if (!r.Success || r.Data == null) return Ok(r);
            return File(r.Data, Xlsx, template ? "mdr-template.xlsx" : "mdr-" + contractId + ".xlsx");
        }

        [HttpPost("mdr/import")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public async Task<IActionResult> ImportAsync([FromForm] EdmsImportForm form, CancellationToken ct)
        {
            if (form?.File == null || form.File.Length == 0) return Ok(ResponseDtoHelper.BadRequest<PcMdrImportResultDto>("فایل انتخاب نشده است."));
            using var ms = new MemoryStream();
            await form.File.CopyToAsync(ms, ct);
            return Ok(await _edms.ImportMdrAsync(form.ContractId, ms.ToArray(), ct));
        }

        // tender register
        [HttpGet("tenders")]
        public async Task<IActionResult> TendersAsync([FromQuery] PcTenderQuery query, CancellationToken ct) => Ok((await _edms.GetTenderPackagesAsync(query, ct)).ToPaginatedResultSafe());

        [HttpGet("tenders/{id:int}")]
        public async Task<IActionResult> TenderAsync(int id, CancellationToken ct) => Ok(await _edms.GetTenderPackageAsync(id, ct));

        [HttpGet("tenders/of-contract/{contractId:int}")]
        public async Task<IActionResult> TendersOfContractAsync(int contractId, CancellationToken ct) => Ok(await _edms.TendersOfContractAsync(contractId, ct));

        [HttpPost("tenders")]
        public async Task<IActionResult> SaveTenderAsync([FromBody] PcTenderPackageSaveDto dto, CancellationToken ct) => Ok(await _edms.SaveTenderPackageAsync(dto, ct));

        [HttpPost("tenders/documents")]
        public async Task<IActionResult> SaveTenderDocumentAsync([FromBody] PcTenderDocumentSaveDto dto, CancellationToken ct) => Ok(await _edms.SaveTenderDocumentAsync(dto, ct));

        [HttpPost("tenders/action")]
        public async Task<IActionResult> TenderActionAsync([FromBody] PcTenderActionDto dto, CancellationToken ct) => Ok(await _edms.TenderActionAsync(dto, ct));
    }

    public class EdmsImportForm
    {
        public int ContractId { get; set; }
        public IFormFile File { get; set; }
    }
}
