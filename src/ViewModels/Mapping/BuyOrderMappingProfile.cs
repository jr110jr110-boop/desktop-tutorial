using System.Linq;
using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.BuyOrderDtos;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.Mapping
{
    public class BuyOrderMappingProfile : Profile
    {
        public BuyOrderMappingProfile()
        {
            CreateMap<AddBuyOrderDto, BuyOrder>();
            CreateMap<UpdateBuyOrderDto, BuyOrder>();
            CreateMap<ResponseUpdateBuyOrderChangeCountDto, BuyOrder>();
            CreateMap<UpdateBuyOrderFinanceDocumentSettedDto, BuyOrder>();
            CreateMap<UpdateBuyOrderFullParametersDto, BuyOrder>();

            CreateMap<BuyOrder, ResponseBuyOrderDto>()
                .ForMember(dest => dest.Files, opt => opt.MapFrom(src => src.BuyOrderFiles))
                .ForMember(dest => dest.BuyOrderProcesses, opt => opt.MapFrom(src => src.BuyOrderProcesses));

            CreateMap<BuyOrder, ResponseBuyOrderCardBoardDto>()
                .ForMember(dest => dest.Processes, opt =>
                    opt.MapFrom(src => src.BuyOrderProcesses.Select(x => x.Process).Where(x => !x.Deleted)))

                .ForMember(dest => dest.WorkOrderTitle, opt => opt.MapFrom(src => src.WorkOrder.Title));

            CreateMap<BuyOrder, ResponseBuyOrderAndProcesses>()
                .ForMember(dest => dest.Files, opt => opt.MapFrom(src => src.BuyOrderFiles))
                .ForMember(dest => dest.Processes, opt =>
                    opt.MapFrom(src => src.BuyOrderProcesses.Select(x => x.Process).Where(x => !x.Deleted)));

            CreateMap<BuyOrderFile, ResponseFileDto>()
              .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Document.Id))
              .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Document.Name))
              .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Document.Code))
              .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Document.Description))
              .ForMember(dest => dest.MimeType, opt => opt.MapFrom(src => src.Document.MimType));
        }
    }
}
