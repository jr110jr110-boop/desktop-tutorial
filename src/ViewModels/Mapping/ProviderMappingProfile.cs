using AutoMapper;
using Entities.Model.NasbNiro.Providers;
using ViewModels.NasbNiro.ProviderDtos;

namespace ViewModels.Mapping
{
    public class ProviderMappingProfile : Profile
    {
        public ProviderMappingProfile()
        {
            CreateMap<AddProviderDto, Provider>()
                .ForMember(dest => dest.ProviderContacts, opt => opt.MapFrom(src => src.ProviderContacts))
                .ForMember(dest => dest.ProviderAccounts, opt => opt.MapFrom(src => src.ProviderAccounts));

            CreateMap<UpdateProviderDto, Provider>();

            CreateMap<Provider, ResponseProviderDto>()
                .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.Category.Category))
                .ForMember(dest => dest.ProviderContacts, opt => opt.MapFrom(src => src.ProviderContacts))
                .ForMember(dest => dest.ProviderAccounts, opt => opt.MapFrom(src => src.ProviderAccounts));

            CreateMap<AddProviderContactDto, ProviderContact>();
            CreateMap<UpdateProviderContactDto, ProviderContact>();
            CreateMap<ProviderContact, ResponseProviderContactDto>();

            CreateMap<AddProviderAccountDto, ProviderAccount>();
            CreateMap<UpdateProviderAccountDto, ProviderAccount>();
            CreateMap<ProviderAccount, ResponseProviderAccountDto>();
        }
    }
}
