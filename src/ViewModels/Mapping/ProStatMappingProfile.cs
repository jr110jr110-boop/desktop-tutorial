using System;
using System.Collections.Generic;
using System.Text;
using AutoMapper;
using Entities.Model.NasbNiro.Contracts;
using ViewModels.NasbNiro.ProvisionalStatementDtos;

namespace ViewModels.Mapping
{
    public class ProStatMappingProfile:Profile
    {
        public ProStatMappingProfile()
        {
            CreateMap<AddProStatDto , ProvisionalStatement>();
            CreateMap<UpdateProStatDto , ProvisionalStatement>();
            CreateMap<ProvisionalStatement, ResponseProStatDto>();
        }
    }
}
