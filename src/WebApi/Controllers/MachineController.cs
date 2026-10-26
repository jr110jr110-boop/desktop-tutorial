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
using ViewModels.NasbNiro.Constract;
using WebApi.Extensions;

namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   create / update: PMO, PRODUCTIONMANAGER, MAINTENANCE, ENGINEERING
    [Route("api/v1/[controller]")]
    [ApiController]
    [Authorize]
    public class MachineController : ControllerBase
    {
        private readonly IMachineService _machineService;

        public MachineController(IMachineService machineService)
        {
            _machineService = machineService;
        }

        [Authorize(Roles = LegacyRoles.MachineWriters)]
        [HttpPost]
        public async Task<ActionResult> Add(MachineInfoViewModel machine)
        {
            int id = await _machineService.Add(machine);
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = id
            });
        }

        [Authorize(Roles = LegacyRoles.MachineWriters)]
        [HttpPut]
        public async Task<ActionResult> Edit(MachineViewModel machine)
        {
            await _machineService.Edit(machine);
            return Ok(new ResponseObjects
            {
                Success = true,
                Message = "",
                Data = machine.Id
            });
        }

        [HttpGet]
        public async Task<ActionResult> Get()
        {
            var list = await _machineService.GetAll();
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = list
            });
        }

        [HttpGet("active")]
        public async Task<ActionResult> GetByActive()
        {
            var list = await _machineService.GetAllActive();
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = list
            });
        }

        [HttpGet("{machineId}")]
        public async Task<ActionResult> GetById(int machineId)
        {
            var workflowGroup = await _machineService.GetById(machineId);
            return Ok(new ResponseObjects()
            {
                Success = true,
                Message = "",
                Data = workflowGroup
            });
        }

        
    }
}