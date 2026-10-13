using System.Linq;
using AutoMapper;
using Entities.Model.NasbNiro.Equipments;
using ViewModels.NasbNiro.EquipmentAssemblyDtos;

namespace ViewModels.Mapping
{
    public class EquipmentAssemblyMappingProfile : Profile
    {
        public EquipmentAssemblyMappingProfile()
        {
            CreateMap<AddEquipmentAssemblyDto, EquipmentAssembly>();
            CreateMap<UpdateEquipmentAssemblyDto, EquipmentAssembly>();

            CreateMap<EquipmentAssembly, ResponseEquipmentAssemblyDto>()
                 .ForMember(dest => dest.Children, opt => opt.Ignore())
                 .ForMember(dest => dest.ParentId, opt => opt.MapFrom(src => src.PredecessorId))
                .ForMember(dest => dest.TotalWeightPercent, opt => opt
                   .MapFrom(src => src
                       .EquipmentBoms
                       .SelectMany(x => x.EquipmentProcesses)
                       .Where(x => !x.Deleted)
                       .Select(x => x.WeightPercent)
                       .Sum()))
                .ForMember(dest => dest.EquipmentBoms,
                    opt => opt.MapFrom(src => src.EquipmentBoms.Where(x => x.Deleted == src.Deleted)))
                .ForMember(dest => dest.Equipment, opt => opt.MapFrom(src => src.Equipment));

            CreateMap<EquipmentAssembly, ResponseEquipmentAssemblyWithoutTypesDto>()
                .ForMember(dest => dest.Children, opt => opt.Ignore())
                .ForMember(dest => dest.ParentId, opt => opt.MapFrom(src => src.PredecessorId))
                .ForMember(dest => dest.TotalWeightPercent, opt => opt
                   .MapFrom(src => src
                       .EquipmentBoms
                       .SelectMany(x => x.EquipmentProcesses)
                       .Where(x => !x.Deleted)
                       .Select(x => x.WeightPercent)
                       .Sum()))
                .ForMember(dest => dest.Equipment, opt => opt.Ignore())
                .ForMember(dest => dest.EquipmentBoms, opt => opt.Ignore());
        }
    }
}
