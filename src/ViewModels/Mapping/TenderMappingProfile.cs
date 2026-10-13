using AutoMapper;
using Entities.Model.NasbNiro.Tenders;
using System.Linq;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.TenderDtos;

namespace ViewModels.Mapping
{
    public class TenderMappingProfile : Profile
    {
        public TenderMappingProfile()
        {
            CreateMap<AddTenderDto, Tender>();
            CreateMap<UpdateTenderDto, Tender>();

            CreateMap<Tender, ResponseTenderDto>()
                .ForMember(dest => dest.Files, opt => opt.MapFrom(contract => contract.TenderFiles))
                .ForMember(dest => dest.Guaranties, opt => opt.MapFrom(contract => contract.TenderGuaranties.Where(x => x.Deleted == false)));

            CreateMap<GuarantyTenderDtoBase, TenderGuaranty>();
            CreateMap<ResponseTenderGuarantyDto, TenderGuaranty>();

            CreateMap<TenderGuaranty, ResponseTenderGuarantyDto>()
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
                .ForMember(dest => dest.TenderId, opt => opt.MapFrom(src => src.TenderId));

            CreateMap<TenderFile, ResponseFileDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Document.Id))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Document.Name))
                .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Document.Code))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Document.Description))
                .ForMember(dest => dest.MimeType, opt => opt.MapFrom(src => src.Document.MimType));
        }
    }
}
