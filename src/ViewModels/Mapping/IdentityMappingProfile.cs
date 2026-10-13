using AutoMapper;
using Entities.Identity;
using ViewModels.Identity.UserDtos;
using ViewModels.NasbNiro;

namespace ViewModels.Mapping
{
    public class IdentityMappingProfile : Profile
    {
        public IdentityMappingProfile()
        {
            CreateMap<User, ResponseUserDto>()
                .ForMember(dest => dest.Roles, opt => opt.MapFrom(src => src.Roles))
                .ForMember(dest => dest.ProfilePhoto, opt => opt.MapFrom(src => src.ProfilePhoto));

            CreateMap<User, ResponseDashboardUserDto>()
                .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => src.Name + " " + src.LastName))
                .ForMember(dest => dest.Roles, opt => opt.MapFrom(src => src.Roles));

            CreateMap<UserRole, ResponseUserRoleDto>();
            CreateMap<UpdateUserDto, User>();

            CreateMap<UserRole, ResponseUserRoleDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.RoleId))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Role.Description))
                .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Role.Name));
        }
    }
}
