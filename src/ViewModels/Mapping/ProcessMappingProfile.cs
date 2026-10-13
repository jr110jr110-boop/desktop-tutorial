using System.Linq;
using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.DocumentDtos;
using ViewModels.NasbNiro.ProcessDtos;

namespace ViewModels.Mapping
{
    public class ProcessMappingProfile : Profile
    {
        public ProcessMappingProfile()
        {
            CreateMap<AddProcessDto, ProcessEntity>();
            CreateMap<UpdateProcessDto, ProcessEntity>();
            CreateMap<UpdateProcessFullParametersDto, ProcessEntity>();

            CreateMap<ProcessEntity, ResponseProcessDto>()
                //.ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.Name + " " + src.User.LastName))
                .ForMember(dest => dest.Files, opt => opt.MapFrom(src => src.ProcessFiles))
                .ForMember(dest => dest.BomCount, opt => opt.MapFrom(src => src.Bom.Count));

            CreateMap<ProcessEntity, ResponseProcessSimpleDto>();
            CreateMap<ProcessEntity, ResponseProcessDashboardDto>();

            CreateMap<ProcessEntity, ResponseProcessCardBoardDto>()
                .ForMember(dest => dest.BomCount, opt => opt.MapFrom(src => src.Bom.Count))
                //.ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.Name + " " + src.User.LastName))
                .ForMember(dest => dest.Files, opt => opt.MapFrom(src => src.ProcessFiles))
                .ForMember(dest => dest.BomTitle, opt => opt.MapFrom(src => src.Bom.Title))
                .ForMember(dest => dest.AssemblyId, opt => opt.MapFrom(src => src.Bom.Assembly.Id))
                .ForMember(dest => dest.AssemblyTitle, opt => opt.MapFrom(src => src.Bom.Assembly.Title))
                .ForMember(dest => dest.WorkOrderId, opt => opt.MapFrom(src => src.Bom.Assembly.WorkOrder.Id))
                .ForMember(dest => dest.WorkOrderTitle, opt => opt.MapFrom(src => src.Bom.Assembly.WorkOrder.Title));

            //        CreateMap<ProcessEntity, ResponseProcessCardBoardDto>()
            //.ForMember(dest => dest.BomCount, opt => opt.MapFrom(src => src.Bom != null ? src.Bom.Count : 0))
            //.ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User != null ? src.User.Name + " " + src.User.LastName : "Unknown"))
            //.ForMember(dest => dest.BomTitle, opt => opt.MapFrom(src => src.Bom != null ? src.Bom.Title : null))
            //.ForMember(dest => dest.AssemblyId, opt => opt.MapFrom(src => src.Bom?.Assembly?.Id))
            //.ForMember(dest => dest.AssemblyTitle, opt => opt.MapFrom(src => src.Bom?.Assembly?.Title))
            //.ForMember(dest => dest.WorkOrderId, opt => opt.MapFrom(src => src.Bom?.Assembly?.WorkOrder?.Id))
            //.ForMember(dest => dest.WorkOrderTitle, opt => opt.MapFrom(src => src.Bom?.Assembly?.WorkOrder?.Title));


            CreateMap<ProcessFile, ResponseFileFinalizeableDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id));
                //.ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Document.Name))
                //.ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Document.Code))
                //.ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Document.Description))
                //.ForMember(dest => dest.MimeType, opt => opt.MapFrom(src => src.Document.MimType));

            CreateMap<ProcessFile, ResponseProcessFileDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
                .ForMember(dest => dest.DocumentId, opt => opt.MapFrom(src => src.DocumentId))
                .ForMember(dest => dest.ProcessId, opt => opt.MapFrom(src => src.ProcessId))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.Major, opt => opt.MapFrom(src => src.Major))
                .ForMember(dest => dest.Minor, opt => opt.MapFrom(src => src.Minor))
                .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.UserId))
                .ForMember(dest => dest.RegDate, opt => opt.MapFrom(src => src.RegDate));

            CreateMap<ProcessFile, ResponseProcessFileStatusDto>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.DocumentId))
                .ForMember(dest => dest.ProcessId, opt => opt.MapFrom(src => src.ProcessId))
                .ForMember(dest => dest.ProcessTitle, opt => opt.MapFrom(src => src.Process.Title))
                .ForMember(dest => dest.BomId, opt => opt.MapFrom(src => src.Process.BomId))
                .ForMember(dest => dest.BomTitle, opt => opt.MapFrom(src => src.Process.Bom.Title))
                .ForMember(dest => dest.AssemblyId, opt => opt.MapFrom(src => src.Process.Bom.Assembly.Id))
                .ForMember(dest => dest.AssemblyTitle, opt => opt.MapFrom(src => src.Process.Bom.Assembly.Title))
                .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
                .ForMember(dest => dest.Major, opt => opt.MapFrom(src => src.Major))
                .ForMember(dest => dest.Minor, opt => opt.MapFrom(src => src.Minor))
                .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.UserId))
                .ForMember(dest => dest.RegDate, opt => opt.MapFrom(src => src.RegDate));
        }
    }
}
