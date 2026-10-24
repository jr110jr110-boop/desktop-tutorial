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

namespace WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocumentService _documentServices;
        private readonly IConfiguration _configuration;


        public DocumentsController(IDocumentService documentServices, IConfiguration configuration)
        {
            _documentServices = (documentServices == null) ? throw new ServiceInvalidException(nameof(documentServices)) : documentServices;
            _configuration = configuration;
        }

        [Authorize]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<ActionResult> UploadAsync([FromQuery] UploadFileDto uploadFileDto)
        {
            return this.Ok(await this._documentServices.UploadDocumentAsync(uploadFileDto));
        }

        [Authorize]
        [HttpPut("change-file")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult> ChangeAsync([FromQuery] Guid documentId, IFormFile file)
        {
            // the document id comes in the query and the file in the multipart body (as the front sends it)
            return Ok(await _documentServices.ChangeFileAsync(new ChangeFileDto { DocumentId = documentId, File = file }));
        }

        [Authorize]
        [HttpPut]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<ActionResult> UpdateDocumentDescriptionAsync([FromBody] UpdateDocumentDto updateDocumentDescriptionDto)
        {
            return this.Ok(await this._documentServices
                    .UpdateDocumentAsync(updateDocumentDescriptionDto));
        }

        [Authorize]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<ActionResult> DownloadAsync([FromQuery] string DocumentId)
        {
            // letter files are served only through the letter, which checks who may read it
            if (!Guid.TryParse(DocumentId, out Guid id) || await _documentServices.IsLetterFileAsync(id))
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
        [HttpGet("get-file-info")]
        [ProducesResponseType(typeof(ResponseDto<ResponseFileDto>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult<ResponseDto<ResponseFileDto>>> GetFileInfoAsync([FromQuery] Guid fileId)
        {
            return Ok(await _documentServices.GetFileInfoAsync(fileId));
        }


    }
}