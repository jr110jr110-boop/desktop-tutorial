using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.NasbNiro.Contracts;
using ViewModels;
using ViewModels.NasbNiro.BomDtos;
using ViewModels.NasbNiro.Pagination;
using ViewModels.NasbNiro.Wrokflow;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write: PMO only
    [Route("api/v1/[controller]")]
    [ApiController]
    [Authorize]
    public class WorkflowController : ControllerBase
    {
        private readonly IWorkflowStationService _workflowStationService;
        public WorkflowController(IWorkflowStationService workflowStationService)
        {
            _workflowStationService = workflowStationService;
        }

        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPost("station")]
        public async Task<ActionResult> Add(WorkflowStationViewModel workflowStation)
        {
            var addedEngineering = await _workflowStationService.Add(workflowStation);
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = addedEngineering
            });
        }

        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPost]
        public async Task<ActionResult> Add(List<WorkflowStationViewModel> workflowStations)
        {
            var addedEngineering = await _workflowStationService.Setter(workflowStations);
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = addedEngineering
            });
        }

        [HttpGet("{workflowGroupId}")]
        public async Task<ActionResult> Get(int workflowGroupId)
        {
            var list = await _workflowStationService.Getter(workflowGroupId);
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = list
            });
        }
    }
}