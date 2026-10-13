using System.Linq;
using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.BomDtos;

namespace ViewModels.Mapping
{
    public class BomMappingProfile : Profile
    {
        public BomMappingProfile()
        {
            CreateMap<AddBomDto, Bom>();
            CreateMap<UpdateBomDto, Bom>();

            CreateMap<Bom, ResponseBomDto>()
                .ForMember(dest => dest.TotalWeightPercent, opt =>
                   opt.MapFrom(src => src.Processes
                       .Where(x => !x.Deleted)
                       .Select(x => x.WeightPercent)
                       .Sum()))
                .ForMember(dest => dest.TotalDuration, opt =>
                    opt.MapFrom(src => src.Processes
                    .Where(x => !x.Deleted)
                    .Select(p => p.MapDuration + p.MasterDuration + p.ProcessDuration)
                    .Sum()));

            CreateMap<Bom, ResponseBomAndChildrenDto>()
                .ForMember(dest => dest.TotalWeightPercent, opt =>
                    opt.MapFrom(src => src.Processes
                    .Where(x => !x.Deleted)
                    .Select(x => x.WeightPercent)
                    .Sum()))
              .ForMember(dest => dest.TotalDuration, opt =>
                    opt.MapFrom(src => src.Processes
                    .Where(x => !x.Deleted)
                    .Select(p => p.MapDuration + p.MasterDuration + p.ProcessDuration)
                    .Sum()))
                .ForMember(dest => dest.Processes, opt =>
                    opt.MapFrom(src =>
                        src.Processes.Where(x => x.Deleted == src.Deleted).OrderBy(x => x.Sort)));
        }
    }
}
