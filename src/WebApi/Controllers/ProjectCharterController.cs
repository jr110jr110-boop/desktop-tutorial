using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels.NasbNiro.ContractDtos;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   create / update the project charter and its file: PMO, PLANNING, COMMERCIAL, PROJECTMANAGER
    [Authorize]
    public class ProjectCharterController : BaseApiController
    {
        private readonly IContractService _contractService;

        public ProjectCharterController(IContractService contractService)
        {
            _contractService = contractService ?? throw new System.ArgumentNullException(nameof(contractService));
        }

        [Authorize(Roles = LegacyRoles.ContractWriters)]
        [HttpPost("add-project-charter-master")]
        [ProducesResponseType(typeof(ResponseDto<ResponseContractDto>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<ResponseContractDto>>> UpdateContractMasterAsync([FromBody] UpdateMasterDto updateMasterDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.UpdateContractMasterAsync(updateMasterDto, cancellationToken));
        }

        [Authorize(Roles = LegacyRoles.ContractWriters)]
        [HttpPost("add-project-charter-file")]
        [ProducesResponseType(typeof(ResponseDto<Guid>), (int)HttpStatusCode.OK)]
        public async ValueTask<ActionResult<ResponseDto<string>>>
            AttachFileToContractAsync([FromQuery] AttachProjectCharterFileDto attachProjectCharterFileDto, CancellationToken cancellationToken)
        {
            return Ok(await _contractService.AttachProjectCharterFileAsync(attachProjectCharterFileDto, cancellationToken));
        }
    }
}
