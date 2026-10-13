//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.Linq;
//using System.Linq.Dynamic.Core;
//using System.Threading;
//using System.Threading.Tasks;
//using AutoMapper;
//using AutoMapper.QueryableExtensions;
//using DataLayer.Context;
//using Entities.Model.NasbNiro.Contracts;
//using Entities.Model.NasbNiro.Providers;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using Services.NasbNiro.Reports;
//using ViewModels.NasbNiro.ProviderDtos;

//namespace WebApi.Controllers
//{

//    public class AA_TestController : BaseApiController
//    {
//        private readonly IUnitOfWork _unitOfWork;
//        private readonly IMaterialBalanceReports _balanceReports;
//        private readonly IMapper _mapper;

//        public AA_TestController(IUnitOfWork unitOfWork, IMaterialBalanceReports balanceReports,IMapper mapper)
//        {
//            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
//            _balanceReports = balanceReports;
//            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
//        }

//        [HttpGet("Providers")]
//        public async Task<IEnumerable<dynamic>> GetProvidersAsync()
//        {
//            var result = await _unitOfWork
//                .Set<Provider>()
//                .ProjectTo<ResponseProviderDto>(_mapper.ConfigurationProvider)
//                .ToListAsync();

//            return result;
//        }


//        [HttpGet("AvailableContractIds")]
//        public async Task<IEnumerable<int>> AvailableContractIdsAsync()
//        {
//            var result = await _unitOfWork.Set<ContractEntity>().Where(x => !x.Deleted).Select(x => x.Id).ToListAsync();

//            return result;
//        }


//        [HttpGet("MaterialBalanceBomId")]
//        public async Task<dynamic> MaterialBalanceBomIdAsync(int bomId, CancellationToken cancellationToken)
//        {
//            return await _balanceReports.GetMaterialBalanceReportByBomIdAsync(bomId, cancellationToken);
//        }


//        [HttpGet("MaterialBalanceEquipmentId")]
//        public async Task<dynamic> MaterialBalanceEquipmentIdAsync(int equipmentId, CancellationToken cancellationToken)
//        {
//            return await _balanceReports.GetAllMaterialBalanceReportsAsync(equipmentId, cancellationToken);
//        }

//    }
//}

