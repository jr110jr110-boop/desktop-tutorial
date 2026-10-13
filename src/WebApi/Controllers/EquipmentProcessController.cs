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
using ViewModels.NasbNiro.EquipmentProcessDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.ProcessDtos;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    [Authorize]
    public class EquipmentProcessController : BaseApiController
    {
        private readonly IEquipmentProcessService _equipmentProcessService;

        public EquipmentProcessController(IEquipmentProcessService EquipmentProcessService)
        {
            _equipmentProcessService = EquipmentProcessService
                ?? throw new System.ArgumentNullException(nameof(EquipmentProcessService));
        }

        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseEquipmentProcessDto>>>
            AddEquipmentProcessAsync([FromBody] AddEquipmentProcessDto addEquipmentProcessDto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.AddEquipmentProcessAsync(addEquipmentProcessDto, cancellationToken));
        }

        //[HttpGet("getFileStatus/{processId:int}")]
        //[ProducesResponseType(typeof(ResponseDto<ResponseEquipmentProcessFileStatusDto>), (int)HttpStatusCode.OK)]
        //public async Task<ActionResult<ResponseProcessFileStatusDto>> GetFileStatusAsync(int processId)
        //{
        //    return Ok(await _equipmentProcessService.GetProcessFileStatusAsync(processId));
        //}

        [HttpGet("getFilesStatusByEquipmentId/{equipmentId:int}")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<ResponseProcessFileStatusDto>>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult<IEnumerable<ResponseProcessFileStatusDto>>> GetFileStatusByEquipmentIdAsync(int equipmentId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.GetProcessesFileStatusByEquipmentIdAsync(equipmentId, cancellationToken));
        }

        [HttpPut("finalizeFile")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async Task<ActionResult<string>> FinalizeFileAsync([FromBody] FinalizeEquipmentProcessFileDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.FinalizeDocumentAsync(dto, cancellationToken));
        }

        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<EquipmentProcessUpdateResponseDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<EquipmentProcessUpdateResponseDto>>>
            UpdateEquipmentProcessAsync([FromBody] UpdateEquipmentProcessDto updateEquipmentProcessDto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.UpdateEquipmentProcessAsync(updateEquipmentProcessDto, cancellationToken));
        }

        [HttpDelete("{equipmentProcessId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseEquipmentProcessDto>>> DeleteEquipmentProcessAsync(
            int equipmentProcessId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.DeleteEquipmentProcessAsync(equipmentProcessId, cancellationToken));
        }

        [HttpGet("get-equipmentProcess")]
        [ProducesResponseType(typeof(ResponseDto<ResponseEquipmentProcessDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseEquipmentProcessDto>>>
            GetEquipmentByIdAsync([FromQuery] GetEquipmentProcessDto getEquipmentProcessDto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.GetResponseEquipmentProcessDtoAsync(getEquipmentProcessDto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseEquipmentProcessDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseEquipmentProcessDto>>>>
            GetAllEquipmentsAsync([FromQuery] EquipmentProcessPaginationParams EquipmentProcessPaginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _equipmentProcessService.GetAllResponseEquipmentProcessDtoAsync(EquipmentProcessPaginationParams, cancellationToken)).ToPaginatedResult());
        }

        [HttpDelete("delete-equipmentProcess-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteEquipmentProcessResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteEquipmentProcessResultDto>>>>
            DeleteEquipmentProcessCollectionAsync(int[] equipmentProcessIds, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.DeleteEquipmentProcessCollectionAsync(equipmentProcessIds, cancellationToken));
        }

        [HttpPost("attach-file-to-equipmentProcess")]
        [ProducesResponseType(typeof(ResponseDto<string>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>> AttachFileToEquipmentProcessAsync(
            [FromQuery] AttachFileToEntityDto attachFileDto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.AttachFileAsync(attachFileDto, cancellationToken));
        }

        [HttpDelete("detach-file-from-equipmentProcess")]
        [ProducesResponseType(typeof(ResponseDto<bool>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<bool>>> DetachFileFromEquipmentProcessAsync(
            int equipmentProcessId, Guid fileId, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.DetachFileAsync(equipmentProcessId, documentId: fileId, cancellationToken: cancellationToken));
        }

        [HttpPut("update-sort")]
        [ProducesResponseType(typeof(ResponseDto<UpdateEquipmentProcessSortResponseDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<UpdateEquipmentProcessSortResponseDto>>>
            UpdateSortAsync(UpdateEquipmentProcessSortDto updateEquipmentProcessSortDto, CancellationToken cancellationToken)
        {
            return Ok(await _equipmentProcessService.UpdateSortAsync(updateEquipmentProcessSortDto, cancellationToken));
        }
    }
}
