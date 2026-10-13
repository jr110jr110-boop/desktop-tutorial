using System.Linq;
using AutoMapper;
using Entities.Model.NasbNiro.Equipments;
using ViewModels.NasbNiro.EquipmentBomDtos;

namespace ViewModels.Mapping
{
    public class EquipmentBomMappingProfile : Profile
    {
        public EquipmentBomMappingProfile()
        {
            CreateMap<AddEquipmentBomDto, EquipmentBom>();
            CreateMap<UpdateEquipmentBomDto, EquipmentBom>();

            CreateMap<EquipmentBom, ResponseEquipmentBomDto>()
                .ForMember(dest => dest.EquipmentProcesses,
                    opt => opt.MapFrom(src =>
                        src.EquipmentProcesses.Where(x => x.Deleted == src.Deleted).OrderBy(x => x.Sort)))
                .ForMember(dest => dest.TotalWeightPercent, opt =>
                   opt.MapFrom(src => src.EquipmentProcesses
                       .Where(x => !x.Deleted)
                       .Select(x => x.WeightPercent)
                       .Sum()))
                .ForMember(dest => dest.TotalDuration, opt =>
                   opt.MapFrom(src => src.EquipmentProcesses
                        .Where(x => !x.Deleted)
                        .Select(x => x.MapDuration + x.MasterDuration + x.ProcessDuration)
                        .Sum()));

            CreateMap<EquipmentBom, ResponseEquipmentBomWithoutTypesDto>()
                .ForMember(dest => dest.EquipmentProcesses, opt => opt.Ignore())
                .ForMember(dest => dest.TotalWeightPercent, opt =>
                   opt.MapFrom(src => src.EquipmentProcesses
                       .Where(x => !x.Deleted)
                       .Select(x => x.WeightPercent)
                       .Sum()))
               .ForMember(dest => dest.TotalDuration, opt =>
                   opt.MapFrom(src => src.EquipmentProcesses
                       .Where(x => !x.Deleted)
                       .Select(x => x.MapDuration + x.MasterDuration + x.ProcessDuration)
                       .Sum()));
        }
    }
}
