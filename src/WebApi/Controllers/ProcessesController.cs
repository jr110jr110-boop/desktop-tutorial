using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    [Authorize]
    public class ProcessesController : BaseApiController
    {
        private readonly IProcessService _processService;

        public ProcessesController(IProcessService ProcessService)
        {
            _processService = ProcessService ?? throw new System.ArgumentNullException(nameof(ProcessService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> AddProcessAsync(
            [FromBody] AddProcessDto addProcessDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.AddProcessAsync(addProcessDto, cancellationToken));
        }

        [HttpPut("finalizeFile")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult<string>> FinalizeFileAsync([FromBody] FinalizeProcessFileDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.FinalizeDocumentAsync(dto, cancellationToken));
        }

        [HttpGet("getFilesStatus/{bomId:int}")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ResponseProcessFileStatusDto>>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult<IEnumerable<ResponseProcessFileStatusDto>>> GetFileStatusByBomIdAsync(
            int bomId, CancellationToken cancellationToken)
        {
            return Ok(await _processService.GetProcessesFileStatusAsync(bomId, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> UpdateProcessAsync(
            [FromBody] UpdateProcessDto updateProcessDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.UpdateProcessAsync(updateProcessDto, cancellationToken));
        }

        [HttpPut("revert-process-status")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> UpdateProcessRevertStatusAsync(
            [FromBody] RevertProcessDto revertProcessDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.RevertStatusAsync(revertProcessDto, cancellationToken));
        }

        [HttpPut("change-user")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> ChangeUserAsync(
            [FromBody] UpdateProcessSelectUserDto updateProcessSelectUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.ChangeUserAsync(updateProcessSelectUserDto, cancellationToken));
        }

        [HttpPut("update-full-parameters")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> UpdateProcessFullParametersAsync(
            [FromBody] UpdateProcessFullParametersDto updateProcessFullParametersDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.UpdateProcessFullParametersAsync(updateProcessFullParametersDto, cancellationToken));
        }

        [HttpPut("pmo-accept-process")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> PmoAcceptProcessAsync(
            [FromBody] UpdateProcessPmoAcceptDto updateProcessMasterAcceptDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.PmoAcceptProcessAsync(updateProcessMasterAcceptDto, cancellationToken));
        }

        [HttpPut("process-has-been-assigned-to-user")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> ProcessAssignedToUserAsync(
            [FromBody] UpdateProcessAssignedToUserDto updateProcessAssignedToUserDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.AssignedToUserAsync(updateProcessAssignedToUserDto, cancellationToken));
        }

        [HttpPut("master-accept-process")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> MasterAcceptProcessAsync(
            [FromBody] UpdateProcessMasterAcceptDto updateProcessMasterAcceptDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.MasterAcceptProcessAsync(updateProcessMasterAcceptDto, cancellationToken));
        }

        [HttpPut("engineer-accept-documents")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> AcceptDocumentsAsync(
            [FromBody] UpdateProcessEngineerAcceptDocumentsDto updateProcessEngineerAcceptDocumentsDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.EngineerAcceptDocumentsAsync(updateProcessEngineerAcceptDocumentsDto, cancellationToken));
        }

        [HttpPut("engineering-accept-documents")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> AcceptDocumentsAsync(
            [FromBody] UpdateProcessEngineeringAcceptDocumentsDto updateProcessAcceptDocumentsDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.EngineeringAcceptDocumentsAsync(updateProcessAcceptDocumentsDto, cancellationToken));
        }

        [HttpPut("engineering-not-accept-documents")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> NotAcceptDocumentsAsync(
            [FromBody] UpdateProcessEngineeringAcceptDocumentsDto updateProcessAcceptDocumentsDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.EngineeringNotAcceptDocumentsAsync(updateProcessAcceptDocumentsDto, cancellationToken));
        }

        [HttpDelete("{processId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> DeleteProcessAsync(
            int processId, CancellationToken cancellationToken)
        {
            return Ok(await _processService.DeleteProcessAsync(processId, cancellationToken));
        }

        [HttpGet("get-process")]
        [ProducesResponseType(typeof(ResponseDto<ResponseProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseProcessDto>>> GetProcessByIdAsync(
            [FromQuery] GetProcessDto getProcessDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.GetResponseProcessDtoAsync(getProcessDto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseProcessDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseProcessDto>>>>
            GetAllProcesssAsync([FromQuery] ProcessPaginationParams ProcessPaginationParams, CancellationToken cancellationToken)
        {
            Console.WriteLine("0");
            var x = await (_processService.GetAllResponseProcessDtoAsync(ProcessPaginationParams, cancellationToken));
            Console.WriteLine("4");
            return Ok(x.ToPaginatedResult());
        }

        [HttpDelete("delete-process-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteProcessResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteProcessResultDto>>>> DeleteProcessCollectionAsync(
            int[] processIds, CancellationToken cancellationToken)
        {
            return Ok(await _processService.DeleteProcessCollectionAsync(processIds, cancellationToken));
        }

        [HttpPost("attach-file-to-process")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> AttachFileToProcessAsync(
            [FromBody] AttachfileToProcessDto attachFileDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.AttachFileToProcessAsync(attachFileDto, cancellationToken));
        }

        [HttpDelete("detach-file-from-process")]
        [ProducesResponseType(typeof(ResponseDto<bool>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DetachFileFromProcessAsync(
            int processId, Guid fileId, CancellationToken cancellationToken)
        {
            return Ok(await _processService.DetachFileFromProcessAsync(processId, documentId: fileId, cancellationToken: cancellationToken));
        }

        [HttpPut("update-sort")]
        [ProducesResponseType(typeof(ResponseDto<UpdateProcessSortResponseDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<UpdateProcessSortResponseDto>>> UpdateSortAsync(
            UpdateProcessSortDto updateProcessSortDto, CancellationToken cancellationToken)
        {
            return Ok(await _processService.UpdateSortAsync(updateProcessSortDto, cancellationToken));
        }
    }
}
