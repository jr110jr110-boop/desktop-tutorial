using System;
using System.Net;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Services.Contracts;
using ViewModels.Exceptions;
using ViewModels.NasbNiro.DocumentDtos;
using WebApi.Filters;

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocumentService _documentServices;
        private readonly IConfiguration _configuration;
        private readonly Services.NasbNiro.Attachments.IAttachmentService _attachments;


        public DocumentsController(IDocumentService documentServices, IConfiguration configuration, Services.NasbNiro.Attachments.IAttachmentService attachments = null)
        {
            _documentServices = (documentServices == null) ? throw new ServiceInvalidException(nameof(documentServices)) : documentServices;
            _configuration = configuration;
            _attachments = attachments;
        }

        [Authorize]
        [HttpPost]
        [RequestSizeLimit(209715200)]   // 200 MB ceiling for the request; the real cap (setting AttachmentMaxMegabytes, 50 MB by default) is refused in Persian by the check below
        [RequestFormLimits(MultipartBodyLengthLimit = 209715200)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<ActionResult> UploadAsync([FromQuery] UploadFileDto uploadFileDto)
        {
            // PB3: the generic upload takes the same file types and size cap as the record attachments
            if (_attachments != null)
            {
                ResponseDto<string> refusal = await _attachments.CheckUploadAsync(uploadFileDto?.File, HttpContext.RequestAborted, string.Equals(uploadFileDto?.Code, Services.NasbNiro.Attachments.AttachmentRules.LetterContextCode, StringComparison.OrdinalIgnoreCase));
                if (refusal != null) return this.Ok(refusal);
            }
            return this.Ok(await this._documentServices.UploadDocumentAsync(uploadFileDto));
        }

        [Authorize]
        [FileGuard(FileGuardKind.Replace, "documentId")]   // R1t D8: a file that a record holds changes only for somebody who may read that record; a loose file only by its uploader or the PMO
        [HttpPut("change-file")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult> ChangeAsync([FromQuery] Guid documentId, IFormFile file)
        {
            // the document id comes in the query and the file in the multipart body (as the front sends it); the new bytes face the same list, cap and signature check as an upload
            if (_attachments != null)
            {
                ResponseDto<string> refusal = await _attachments.CheckUploadAsync(file, HttpContext.RequestAborted);
                if (refusal != null) return Ok(refusal);
            }
            return Ok(await _documentServices.ChangeFileAsync(new ChangeFileDto { DocumentId = documentId, File = file }));
        }

        [Authorize]
        [FileGuard(FileGuardKind.Edit, "updateDocumentDescriptionDto")]
        [HttpPut]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<ActionResult> UpdateDocumentDescriptionAsync([FromBody] UpdateDocumentDto updateDocumentDescriptionDto)
        {
            // R1a2 (H3): the generic edit; a file that a record refers to changes only through its owner module, a loose file only by its uploader or the PMO
            return this.Ok(await this._documentServices
                    .UpdateDocumentByUserAsync(updateDocumentDescriptionDto));
        }

        [Authorize]
        [FileGuard(FileGuardKind.Download, "DocumentId")]   // R1t D8: the owning module's read rule (a letter's file: the letter's rule), else the uploader and the PMO
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<ActionResult> DownloadAsync([FromQuery] string DocumentId)
        {
            // who may read the file was decided by the guard above (a letter's file by the letter's own rule: a party gets it, a stranger gets 403)
            if (!Guid.TryParse(DocumentId, out Guid id))
            {
                return NotFound("فایلی یافت نشد");
            }

            ResponseDocumentDto document = await this._documentServices
                .DownloadDocumentAsync(Guid.Parse(DocumentId));

            if (document == null)
            {
                return NotFound("فایلی یافت نشد");
            }


            return File(document.FileStream, "application/x-download", document.Name);
        }

        [Authorize]
        [FileGuard(FileGuardKind.Info, "fileId")]   // the name, description and hash of a file never reach somebody who may not read the file
        [HttpGet("get-file-info")]
        [ProducesResponseType(typeof(ResponseDto<ResponseFileDto>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult<ResponseDto<ResponseFileDto>>> GetFileInfoAsync([FromQuery] Guid fileId)
        {
            return Ok(await _documentServices.GetFileInfoAsync(fileId));
        }


    }
}