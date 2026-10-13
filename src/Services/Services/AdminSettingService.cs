using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AutoMapper;
using Common.GuardToolkit;
using DataLayer.Context;
using Entities.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Services.Contracts;
using Services.Resources;
using ViewModels;
using ViewModels.Dto;
using ViewModels.Exceptions;


namespace Services.Services
{
    public class AdminSettingService : IAdminSettingService
    {
        private readonly IMapper _mapper;
        private readonly IWebHostEnvironment _hostingEnvironment;
        IUnitOfWork _uow;

        readonly DbSet<AdminSetting> _adminSetting;

        public AdminSettingService(
            IUnitOfWork uow,
            IWebHostEnvironment hostingEnvironment,
            IMapper mapper)
        {
            this._hostingEnvironment = (hostingEnvironment == null) ?
                throw new RepositoryInvalidException(nameof(hostingEnvironment))
                : hostingEnvironment;

            _uow = uow;
            _uow.ThrowIfNull(nameof(_uow));

            _adminSetting = _uow.Set<AdminSetting>();

            _mapper = mapper;
        }


        public async Task<AdminSettingListDto> GetAdminSettingListAsync(DataTableAjaxPostModel dataTable)
        {
            int pageSize = Convert.ToInt32(dataTable.pageSize == 0 ? 10 : dataTable.pageSize);
            int skip = Convert.ToInt32(dataTable.pageNumber);
            string sortBy = "Id";
            string sortDir = "desc";

            if (dataTable.column != null)
            {
                sortBy = dataTable.column;
            }

            if (dataTable.dir != null)
            {
                sortDir = dataTable.dir.ToLower();
            }

            var users = _adminSetting.AsQueryable();

            if (!string.IsNullOrWhiteSpace(dataTable.search))
            {
                users = users.Where(x => x.BrokerageName.Contains(dataTable.search) || x.RegistrationSMSText.Contains(dataTable.search));
            }

            List<AdminSettingVM> _adminSettings = await users.Select(setting => new AdminSettingVM
            {
                Id = setting.Id,
                BrokerageName = setting.BrokerageName,
                BrokerageLogo = setting.BrokerageLogo,
                LoginType = setting.LoginType,
                IsActiveRegistrationSMS = setting.IsActiveRegistrationSMS,
                RegistrationSMSText = setting.RegistrationSMSText,
                ERegistrationScore = setting.ERegistrationScore,
                InPersonRegistrationScore = setting.InPersonRegistrationScore,
                ProfileCompletionScore = setting.ProfileCompletionScore,
                ScoreOfIntroducingNewCustomer = setting.ScoreOfIntroducingNewCustomer,
                ModifyDate = setting.ModifyDate.ToPersianDateTime().ToString().Split(' ')[0],
                ModifierUserId = setting.ModifierUserId,
                ModifierUserFullName = setting.ModifierUser == null ? "" : (setting.ModifierUser.DisplayName),
                BrokerageCode = setting.BrokerageCode,
                BrokerageBranchCode = setting.BrokerageBranchCode,
                TransactionPercentage = setting.TransactionPercentage,
                ScorePrice = setting.ScorePrice
            })
           .OrderBy(sortBy + " " + sortDir)
           .Skip(skip)
           .Take(pageSize)
           .ToListAsync();


            return new AdminSettingListDto { Count = users.Count(), List = _adminSettings };
        }

        public async Task<int> AddAdminSettingAsync(AdminSettingVM model)
        {
            var setting = _adminSetting.FirstOrDefault();

            if (setting != null)
            {
                throw new RecordNotFoundException(Messages.Notfound);
            }

            var _setting = new AdminSetting(
                    model.BrokerageName,
                    model.BrokerageLogo,
                    model.LoginType,
                    model.IsActiveRegistrationSMS,
                    model.RegistrationSMSText,
                    model.ERegistrationScore,
                    model.InPersonRegistrationScore,
                    model.ProfileCompletionScore,
                    model.ScoreOfIntroducingNewCustomer,
                    DateTime.Now,
                    model.ModifierUserId,
                    model.BrokerageCode,
                    model.BrokerageBranchCode,
                    model.TransactionPercentage,
                    model.ScorePrice,
                    model.TransactionPercentageGroup2,
                    model.TransactionPercentageGroupSell1,
                    model.TransactionPercentageGroup3,
                    model.TransactionPercentageGroupSell2,
                    model.TransactionPercentageGroupSell3
            );

            await _adminSetting.AddAsync(_setting);
            return await _uow.SaveChangesAsync();
        }

        public async Task UpdateAdminSettingAsync(AdminSettingVM model)
        {
            var setting = await _adminSetting.FirstOrDefaultAsync(x => x.Id == model.Id);
            if (setting == null)
            {
                throw new RecordNotFoundException(Messages.Notfound);
            }

            setting.Update(
                    model.BrokerageName,
                    model.BrokerageLogo,
                    model.LoginType,
                    model.IsActiveRegistrationSMS,
                    model.RegistrationSMSText,
                    model.ERegistrationScore,
                    model.InPersonRegistrationScore,
                    model.ProfileCompletionScore,
                    model.ScoreOfIntroducingNewCustomer,
                    DateTime.Now,
                    model.ModifierUserId,
                    model.BrokerageCode,
                    model.BrokerageBranchCode,
                    model.TransactionPercentage,
                    model.ScorePrice,
                    model.TransactionPercentageGroup2,
                    model.TransactionPercentageGroupSell1,
                    model.TransactionPercentageGroup3,
                    model.TransactionPercentageGroupSell2,
                    model.TransactionPercentageGroupSell3
                );
            _adminSetting.Update(setting);
            await _uow.SaveChangesAsync();
        }

        public async Task<AdminSettingVM> GetAdminSettingAsync()
        {
            return await _adminSetting.Select(
                setting => new AdminSettingVM
                {
                    Id = setting.Id,
                    BrokerageName = setting.BrokerageName,
                    BrokerageLogo = setting.BrokerageLogo,
                    LoginType = setting.LoginType,
                    IsActiveRegistrationSMS = setting.IsActiveRegistrationSMS,
                    RegistrationSMSText = setting.RegistrationSMSText,
                    ERegistrationScore = setting.ERegistrationScore,
                    InPersonRegistrationScore = setting.InPersonRegistrationScore,
                    ProfileCompletionScore = setting.ProfileCompletionScore,
                    ScoreOfIntroducingNewCustomer = setting.ScoreOfIntroducingNewCustomer,
                    ModifyDate = setting.ModifyDate.ToPersianDateTime().ToString().Split(' ')[0],
                    ModifierUserId = setting.ModifierUserId,
                    ModifierUserFullName = setting.ModifierUser == null ? "" : (setting.ModifierUser.DisplayName),
                    BrokerageCode = setting.BrokerageCode,
                    BrokerageBranchCode = setting.BrokerageBranchCode,
                    ScorePrice = setting.ScorePrice,
                    TransactionPercentage = setting.TransactionPercentage,
                    TransactionPercentageGroup2 = setting.TransactionPercentageGroup2,
                    TransactionPercentageGroup3 = setting.TransactionPercentageGroup3,
                    TransactionPercentageGroupSell1 = setting.TransactionPercentageGroupSell1,
                    TransactionPercentageGroupSell2 = setting.TransactionPercentageGroupSell2,
                    TransactionPercentageGroupSell3 = setting.TransactionPercentageGroupSell3
                }
                ).FirstOrDefaultAsync();
        }

    }
}
