using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.InquiryDtos;

namespace ViewModels.Mapping
{
    public class InquiryMappingProfile : Profile
    {
        public InquiryMappingProfile()
        {
            CreateMap<AddInquiryDto, Inquiry>();
            CreateMap<UpdateInquiryDto, Inquiry>();

            CreateMap<Inquiry, ResponseInquiryDto>()
                .ForMember(dest => dest.Provider, opt => opt.MapFrom(src => src.Provider));
        }
    }
}
