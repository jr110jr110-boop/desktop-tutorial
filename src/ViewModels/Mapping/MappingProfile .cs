using System.Linq;
using AutoMapper;
using Entities.Model;
using Entities.Model.NasbNiro.ContractEquipments;
using Entities.Model.NasbNiro.Contracts;
using Entities.Model.NasbNiro.Equipments;
using Entities.Model.NasbNiro.Reports;
using ViewModels.NasbNiro.ContractEquipmentDtos;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.EquipmentDtos;
using ViewModels.NasbNiro.ReportsDtos;

namespace ViewModels.Mapping
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<ProgressReport, ProgressReportDto>();
            CreateMap<ProgressReport, ResponseProgressReportDto>();

            CreateMap<Document, ResponseFileDto>()
               .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
               .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
               .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Code))
               .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
               .ForMember(dest => dest.MimeType, opt => opt.MapFrom(src => src.MimType));

            #region ContractEquipment
            CreateMap<AddContractEquipmentDto, ContractEquipment>();
            CreateMap<UpdateContractEquipmentDto, ContractEquipment>();
            CreateMap<ContractEquipment, ResponseContractEquipmentDto>()
                .ForMember(dest => dest.EquipmentTitle, opt => opt.MapFrom(src => src.Equipment.Title));
            #endregion

            #region Equipment
            CreateMap<AddEquipmentDto, Equipment>();
            CreateMap<UpdateEquipmentDto, Equipment>();

            //CreateMap<Equipment, ResponseEquipmentDto>()
            //    .ForMember(dest => dest.EquipmentAssemblies,
            //        opt => opt.MapFrom(src => src.EquipmentAssemblies));

            CreateMap<Equipment, ResponseEquipmentDto>()
                .ForMember(dest => dest.EquipmentAssemblies,
                    opt => opt.MapFrom(src => src.EquipmentAssemblies.Where(x => x.Deleted == src.Deleted)));

            CreateMap<Equipment, ResponseEquipmentWithoutTypesDto>()
                .ForMember(dest => dest.EquipmentAssemblies, opt => opt.Ignore());
            #endregion

            #region Copy Equipments to Contract=>WorkOrders
            CreateMap<EquipmentAssembly, AssemblyEntity>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Predecessor, opt => opt.Ignore())
                .ForMember(dest => dest.PredecessorId, opt => opt.Ignore())
                .ForMember(dest => dest.Deleted, opt => opt.Ignore())
                .ForMember(dest => dest.WorkOrder, opt => opt.Ignore())
                .ForMember(dest => dest.Boms, opt => opt.Ignore());

            CreateMap<EquipmentBom, Bom>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.EquipmentBomId, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.PredecessorId, opt => opt.Ignore())
                .ForMember(dest => dest.Predecessor, opt => opt.Ignore())
                .ForMember(dest => dest.Deleted, opt => opt.Ignore())
                .ForMember(dest => dest.Assembly, opt => opt.Ignore())
                .ForMember(dest => dest.Processes, opt => opt.Ignore()); ;

            CreateMap<EquipmentProcess, ProcessEntity>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Deleted, opt => opt.Ignore())
                .ForMember(dest => dest.ProcessFiles, opt => opt.Ignore());
            //.ForMember(dest => dest.Bom, opt => opt.Ignore())
            //.ForMember(dest => dest.ProcessFiles, opt => opt.MapFrom(src => src.EquipmentProcessFiles));
            #endregion
        }
    }
}
