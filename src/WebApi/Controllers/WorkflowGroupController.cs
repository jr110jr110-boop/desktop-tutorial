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
    [Route("api/v1/[controller]")]
    [ApiController]
    [Authorize]
    public class WorkflowGroupController : ControllerBase
    {
        private readonly IWorkflowGroupService _workflowGroupService;

        public WorkflowGroupController(IWorkflowGroupService workflowGroupService)
        {
            _workflowGroupService = workflowGroupService;
        }

        [HttpPost]
        public async Task<ActionResult> Add(WorkflowGroupInfoViewModel group)
        {
            int id = await _workflowGroupService.Add(group);
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = id
            });
        }

        [HttpGet]
        public async Task<ActionResult> Get()
        {
            var list = await _workflowGroupService.GetAll();
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = list
            });
        }

        [HttpGet("{workflowGroupId}")]
        public async Task<ActionResult> GeTById(int workflowGroupId)
        {
            var workflowGroup = await _workflowGroupService.GetById(workflowGroupId);
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = workflowGroup
            });
        }

        [HttpPut]
        public async Task<ActionResult> Edit(WorkflowGroupViewModel workflowGroup)
        {
            await _workflowGroupService.Edit(workflowGroup);
            return Ok(new ResponseObjects
            {
                Success = true,
                Message = "",
                Data = workflowGroup.Id
            });
        }
    }
}