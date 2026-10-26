using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.AssemblyDtos;
using ViewModels.NasbNiro.Pagination;
using WebApi.Extensions;

using AssemblyResult = System.Threading.Tasks.ValueTask<
    Microsoft.AspNetCore.Mvc.ActionResult<
        Common.Results.ResponseDto<
            ViewModels.NasbNiro.AssemblyDtos.ResponseAssemblyDto>>>;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   definitions (create / update / predecessor / change-status / start-assembling / delete): PMO, ENGINEERING, ENGINEER
    //   assembly stages follow the card board: assembler-assembled, assembler-final-assembled: PMO, ASSEMBLER;  qc-accepted-to-final-assemble, qc-reject-to-assemble, qc-reject-to-final-assemble, qc-accepted-to-final-acceptance: PMO, QC;  pmo-accepted-to-completed: PMO only
    [Authorize]
    public class AssembliesController : BaseApiController
    {
        private readonly IAssemblyService _assemblyService;

        public AssembliesController(IAssemblyService AssemblyService)
        {
            _assemblyService = AssemblyService ?? throw new System.ArgumentNullException(nameof(AssemblyService));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async AssemblyResult AddAssemblyAsync([FromBody] AddAssemblyDto addAssemblyDto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.AddAssemblyAsync(addAssemblyDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async AssemblyResult UpdateAssemblyAsync([FromBody] UpdateAssemblyDto updateAssemblyDto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.UpdateAssemblyAsync(updateAssemblyDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut("set-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async AssemblyResult SetPredecessorAsync([FromBody] UpdateAssemblySetPredecessorDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.SetPredecessorAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut("delete-predecessor")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async AssemblyResult DeletePredecessorAsync(int assemblyId, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.DeletePredecessorAsync(assemblyId, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut("change-status")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async AssemblyResult ChangeStatusAsync([FromBody] UpdateAssemblyChangeStatusDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.ChangeStatusAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpDelete("{assemblyId:int}")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>> DeleteAssemblyAsync(int assemblyId, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.DeleteAssemblyAsync(assemblyId, cancellationToken));
        }

        [HttpGet("get-assembly")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>> GetAssemblyAsync(
            [FromQuery] GetAssemblyDto getAssemblyDto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.GetResponseAssemblyDtoAsync(getAssemblyDto, cancellationToken));
        }

        [HttpGet]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseAssemblyDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseAssemblyDto>>>>
            GetAssemblyAllsAsync([FromQuery] AssemblyPaginationParams assemblyPaginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _assemblyService.GetAllResponseAssemblyDtoAsync(assemblyPaginationParams, cancellationToken)).ToPaginatedResult());
        }


        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpDelete("delete-assembly-collection")]
        [ProducesResponseType(typeof(ResponseDto<IEnumerable<DeleteAssemblyResultDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<IEnumerable<DeleteAssemblyResultDto>>>>
            DeleteAssemblyCollectionAsync([FromBody] int[] assemblyIds, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.DeleteAssemblyCollectionAsync(assemblyIds, cancellationToken));
        }

        [HttpGet("get-ready-to-assemble-assemblies")]
        [ProducesResponseType(typeof(ResponseDto<PaginatedResult<ResponseAssemblyDto>>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<PaginatedResult<ResponseAssemblyDto>>>>
            GetReadyToAssemleAssembliesAsync([FromQuery] ReadyToAssembleAssembliesPaginationParams paginationParams, CancellationToken cancellationToken)
        {
            return Ok((await _assemblyService.GetReadyToAssembleAssembliesAsync(paginationParams, cancellationToken)).ToPaginatedResult());
        }

        [Authorize(Roles = LegacyRoles.EngineeringWriters)]
        [HttpPut("start-assembling")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>>
          StartAssemblingAsync([FromBody] UpdateAssemblyStartAssemblingDto updateAssemblyStartAssemblingDto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.StartAssemblingAsync(updateAssemblyStartAssemblingDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.AssemblyAssembler)]
        [HttpPut("assembler-assembled")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>>
          AssemblerAssembledAsync([FromBody] UpdateAssemblyAssemblerAssembledDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.AssemblerAssembledAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.AssemblyQuality)]
        [HttpPut("qc-accepted-to-final-assemble")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>>
            QcAcceptedToFinalAssembleAsync([FromBody] UpdateAssemblyQcAcceptToFinalAssembleDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.QcAcceptToFinalAssembleAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.AssemblyQuality)]
        [HttpPut("qc-reject-to-assemble")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>>
            QcRejectToAssembleAsync([FromBody] UpdateAssemblyQcRejectToAssembleDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.QcRejectToAssembleAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.AssemblyAssembler)]
        [HttpPut("assembler-final-assembled")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>>
            AssemblerFinalAssembledAsync([FromBody] UpdateAssemblyAssemblerFinalAssembledDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.AssemblerFinalAssembledAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.AssemblyQuality)]
        [HttpPut("qc-reject-to-final-assemble")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>>
            QcRejectToFinalAssembleAsync([FromBody] UpdateAssemblyQcRejectToFinalAssembleDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.QcRejectToFinalAssembleAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.AssemblyQuality)]
        [HttpPut("qc-accepted-to-final-acceptance")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseAssemblyDto>>>
            QcAcceptedToFinalAcceptanceAsync([FromBody] UpdateAssemblyQcAcceptToFinalAcceptanceDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.QcAcceptToFinalAcceptanceAsync(dto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPut("pmo-accepted-to-completed")]
        [ProducesResponseType(typeof(ResponseDto<ResponseAssemblyDto>), (int)HttpStatusCode.OK)]
        public async AssemblyResult QcAcceptedToFinalAcceptanceAsync(
            [FromBody] UpdateAssemblyPmoAcceptToCompletedDto dto, CancellationToken cancellationToken)
        {
            return Ok(await _assemblyService.PmoAcceptToCompletedAsync(dto, cancellationToken));
        }
    }
}
