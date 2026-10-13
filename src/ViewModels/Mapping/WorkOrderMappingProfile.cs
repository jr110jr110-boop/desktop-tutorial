using System.Linq;
using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.WorkOrderDtos;

namespace ViewModels.Mapping
{
    public class WorkOrderMappingProfile : Profile
    {
        public WorkOrderMappingProfile()
        {
            CreateMap<AddWorkOrderDto, WorkOrder>();
            CreateMap<UpdateWorkOrderDto, WorkOrder>();
            CreateMap<WorkOrder, ResponseWorkOrderDto>();

            CreateMap<WorkOrder, ResponseWorkOrderAndChildrenDto>()
                .ForMember(
                    dest => dest.Assemblies,
                    opt => opt.MapFrom(src => src.Assemblies.Where(x => x.Deleted == src.Deleted)));
        }
    }
}
