using System.Linq;
using AutoMapper;
using Entities.Model.NasbNiro.Equipments;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.EquipmentProcessDtos;

namespace ViewModels.Mapping
{
    public class EquipmentProcessMappingProfile : Profile
    {
        public EquipmentProcessMappingProfile()
        {
            CreateMap<AddEquipmentProcessDto, EquipmentProcess>();
            CreateMap<UpdateEquipmentProcessDto, EquipmentProcess>();

            CreateMap<EquipmentProcess, ResponseEquipmentProcessDto>()
                .ForMember(dest => dest.EquipmentBom, opt => opt.MapFrom(src => src.EquipmentBom))
                .ForMember(dest => dest.Files, opt => opt.MapFrom(contract => contract.EquipmentProcessFiles));

            CreateMap<EquipmentProcess, EquipmentProcessUpdateResponseDto>()
                .ForMember(
                    dest => dest.AssemblyWeightPercent,
                    opt => opt.MapFrom(src => src
                        .EquipmentBom
                        .EquipmentAssembly
                        .EquipmentBoms
                        .Where(x => !x.Deleted)
                        .SelectMany(x => x.EquipmentProcesses)
                        .Where(x => !x.Deleted)
                        .Select(x => x.WeightPercent)
                        .Sum()))
                .ForMember(
                    dest => dest.BomWeightPercent,
                    opt => opt.MapFrom(src => src
                        .EquipmentBom
                        .EquipmentProcesses
                        .Where(x => !x.Deleted)
                        .Select(x => x.WeightPercent)
                        .Sum()))
                .ForMember(dest => dest.EquipmentBom, opt => opt.MapFrom(src => src.EquipmentBom))
                .ForMember(dest => dest.Files, opt => opt.MapFrom(contract => contract.EquipmentProcessFiles));

            CreateMap<EquipmentProcess, ResponseEquipmentProcessWithoutTypesDto>()
                .ForMember(dest => dest.EquipmentBom, opt => opt.Ignore())
                .ForMember(dest => dest.Files, opt => opt.MapFrom(contract => contract.EquipmentProcessFiles));

            CreateMap<EquipmentProcessFile, ResponseFileFinalizeableDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.DocumentId))
               .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.EquipmentProcess.DocumentCode))
               .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description));

            CreateMap<EquipmentProcessFile, ResponseEquipmentProcessFileStatusDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.DocumentId))
                .ForMember(dest => dest.ProcessId, opt => opt.MapFrom(src => src.EquipmentProcessId))
                .ForMember(dest => dest.ProcessTitle, opt => opt.MapFrom(src => src.EquipmentProcess.Title))
                .ForMember(dest => dest.BomId, opt => opt.MapFrom(src => src.EquipmentProcess.EquipmentBomId))
                .ForMember(dest => dest.BomTitle, opt => opt.MapFrom(src => src.EquipmentProcess.EquipmentBom.Title))
                .ForMember(dest => dest.AssemblyId, opt => opt.MapFrom(src => src.EquipmentProcess.EquipmentBom.EquipmentAssemblyId))
                .ForMember(dest => dest.AssemblyTitle, opt => opt.MapFrom(src => src.EquipmentProcess.EquipmentBom.EquipmentAssembly.Title));
        }
    }
}
