using System;
using System.Threading.Tasks;
using Common.GuardToolkit;
using DataLayer.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Contracts.Identity;
using Services.Token;
using ViewModels.Dto;


namespace WebApi.Controllers
{
    // R1a role matrix ★ to be confirmed by the company (write actions only; reads stay [Authorize] for every signed-in user; the CEO is not a writer):
    //   every write: PMO only
    [Route("api/v1/AdminSettings")]
    [EnableCors("CorsPolicy")]
    [ApiController]
    [Authorize]
    public class AdminSettingController : Controller
    {
        private readonly IAdminSettingService _adminSettingService;
        private readonly IApplicationUserManager _usersService;
        private readonly IUnitOfWork _uow;
        private readonly IApplicationRoleManager _roleManager;


        public AdminSettingController(
               IApplicationUserManager usersService,
               IAdminSettingService adminSettingService,
               IApplicationRoleManager roleManager,
               IUnitOfWork uow,
               ITokenStoreService tokenStoreService,
               ITokenFactoryService tokenFactoryService
             )
        {
            _usersService = usersService.ThrowIfNull(nameof(usersService));
            _adminSettingService = adminSettingService.ThrowIfNull(nameof(adminSettingService));
            _uow = uow.ThrowIfNull(nameof(_uow));
            _roleManager = roleManager.ThrowIfNull(nameof(_roleManager));
        }



        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPost]
        [ProducesResponseType(typeof(ResponseObjectDto), 200)]
        public async Task<ActionResult> CreateAdminSettingAsync([FromBody] AdminSettingVM model)
        {
            if (model == null)
            {
                return BadRequest(new ResponseObjectDto
                {
                    Message = "اطلاعات ارسالی نامعتبر می باشد .",
                    Success = false,
                    Data = null
                });
            }

            var current_UserId = _usersService.GetCurrentUserId();
            model.ModifierUserId = Convert.ToInt32(current_UserId);


            var setting = await _adminSettingService.GetAdminSettingAsync();
            if (setting != null)
            {
                return BadRequest(new ResponseObjectDto
                {
                    Message = "تنظیمات قبلا در سامانه ثبت شده است . امکان درج تنظیمات جدید وجود ندارد .",
                    Success = false,
                    Data = null
                });

            }

            var newSettingId = await _adminSettingService.AddAdminSettingAsync(model);

            if (newSettingId > 0)
            {
                return Ok(new ResponseObjectDto
                {
                    Message = "ثبت تظیمات با موفقیت انجام شد .",
                    Success = true,
                    Data = true
                });
            }
            else
            {
                return BadRequest(new ResponseObjectDto
                {
                    Message = "خطایی در ثبت تنظیمات رخ داده است .",
                    Success = false,
                    Data = null
                });
            }
        }


        [Authorize(Roles = LegacyRoles.Pmo)]
        [HttpPut]
        [ProducesResponseType(typeof(ResponseObjectDto), 200)]
        public async Task<ActionResult> EditAdminSettingAsync([FromBody] AdminSettingVM model)
        {
            if (model == null || model.Id <= 0)
            {
                return BadRequest(new ResponseObjectDto
                {
                    Message = "اطلاعات ارسالی نامعتبر می باشد .",
                    Success = false,
                    Data = null
                });
            }

            var current_UserId = Convert.ToInt32(_usersService.GetCurrentUserId());
            model.ModifierUserId = current_UserId;

            var setting = await _adminSettingService.GetAdminSettingAsync();
            if (setting != null && setting.Id != model.Id)
            {
                return BadRequest(new ResponseObjectDto
                {
                    Message = "شناسه ارسالی برای تنظیمات نامعتبر می باشد .",
                    Success = false,
                    Data = null
                });
            }

            await _adminSettingService.UpdateAdminSettingAsync(model);

            return Ok(new ResponseObjectDto
            {
                Message = "تنظیمات با موفقیت بروز رسانی شد .",
                Success = true,
                Data = true
            });

        }


        [HttpGet]
        [ProducesResponseType(typeof(ResponseObjectDto), 200)]
        public async Task<ActionResult> GetAdminSettingAsync()
        {
            return this.Ok(new ResponseObjectDto
            {
                Data = await this._adminSettingService.GetAdminSettingAsync(),
                Message = "دریافت تنظیمات با موفقیت انجام شد .",
                Success = true
            });
        }


    }
}
