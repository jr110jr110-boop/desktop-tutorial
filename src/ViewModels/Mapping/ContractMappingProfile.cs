using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using System.Linq;
using ViewModels.NasbNiro.ContractDtos;
using ViewModels.NasbNiro.DocumentDtos;

namespace ViewModels.Mapping
{
    public class ContractMappingProfile : Profile
    {
        public ContractMappingProfile()
        {
            CreateMap<AddContractDto, ContractEntity>();
            CreateMap<UpdateContractDto, ContractEntity>();

            CreateMap<ContractEntity, ResponseContractDto>()
                .ForMember(dest => dest.ProjectCharterFile, opt =>
                    opt.MapFrom(src => src.ProjectCharterFile))
                .ForMember(dest => dest.Files, opt => opt.MapFrom(contract => contract.ContractFiles))
                .ForMember(dest => dest.Guaranties, opt => opt.MapFrom(contract => contract.ContractGuaranties.Where(x => x.Deleted == false)));

            CreateMap<ContractFile, ResponseFileDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Document.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Document.Name))
                .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Document.Code))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Document.Description))
                .ForMember(dest => dest.MimeType, opt => opt.MapFrom(src => src.Document.MimType));

            CreateMap<GuarantyContractDtoBase, ContractGuaranty>();
            CreateMap<ResponseContractGuarantyDto, ContractGuaranty>();

            CreateMap<ContractGuaranty, ResponseContractGuarantyDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Title))
                .ForMember(dest => dest.Bank, opt => opt.MapFrom(src => src.Bank))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.No, opt => opt.MapFrom(src => src.No))
                .ForMember(dest => dest.RegDate, opt => opt.MapFrom(src => src.RegDate))
                .ForMember(dest => dest.Price, opt => opt.MapFrom(src => src.Price))
                .ForMember(dest => dest.Deposite, opt => opt.MapFrom(src => src.Deposite))
                .ForMember(dest => dest.ChequeNo, opt => opt.MapFrom(src => src.ChequeNo))
                .ForMember(dest => dest.SentDate, opt => opt.MapFrom(src => src.SentDate))
                .ForMember(dest => dest.CancelDate, opt => opt.MapFrom(src => src.CancelDate))
                .ForMember(dest => dest.Extention, opt => opt.MapFrom(src => src.Extention))
                .ForMember(dest => dest.DueDate, opt => opt.MapFrom(src => src.DueDate))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.ContractEntityId, opt => opt.MapFrom(src => src.ContractEntityId));
        }
    }
}
