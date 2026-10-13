using System.Linq;
using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.AssemblyDtos;

namespace ViewModels.Mapping
{
    public class AssemblyMappingProfile : Profile
    {
        public AssemblyMappingProfile()
        {
            CreateMap<AddAssemblyDto, AssemblyEntity>();
            CreateMap<UpdateAssemblyDto, AssemblyEntity>();

            CreateMap<AssemblyEntity, ResponseAssemblyDto>()
                .ForMember(dest => dest.Children, opt => opt.Ignore())
                .ForMember(dest => dest.ParentId, opt => opt.MapFrom(src => src.PredecessorId))
                .ForMember(dest => dest.TotalWeightPercent, opt => opt
                   .MapFrom(src => src
                       .Boms
                       .SelectMany(x => x.Processes)
                       .Where(x => !x.Deleted)
                       .Select(x => x.WeightPercent)
                       .Sum()));

            CreateMap<AssemblyEntity, ResponseAssemblyAndChildrenDto>()
                .ForMember(dest => dest.Children, opt => opt.Ignore())
                .ForMember(dest => dest.ParentId, opt => opt.MapFrom(src => src.PredecessorId))
                .ForMember(dest => dest.TotalWeightPercent, opt => opt
                   .MapFrom(src => src
                       .Boms
                       .SelectMany(x => x.Processes)
                       .Where(x => !x.Deleted)
                       .Select(x => x.WeightPercent)
                       .Sum()))
                .ForMember(dest => dest.Boms,
                    opt => opt.MapFrom(src => src.Boms.Where(x => x.Deleted == src.Deleted)));

            CreateMap<AssemblyEntity, ResponseAssemblyDashboardDto>();
        }
    }
}
