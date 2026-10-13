using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.BuyOrderProcesses;

namespace ViewModels.Mapping
{
    public class BuyOrderProcessesMappingProfile : Profile
    {
        public BuyOrderProcessesMappingProfile()
        {
            CreateMap<AddBuyOrderProcessesDto, BuyOrderProcesses>();
            CreateMap<UpdateBuyOrderProcessesDto, BuyOrderProcesses>();
            CreateMap<BuyOrderProcesses, ResponseBuyOrderProcessesDto>();
        }
    }
}
