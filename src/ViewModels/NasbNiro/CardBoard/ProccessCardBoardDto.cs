using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.CardBoard
{
    public class ProccessCardBoardDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public List<ProccessCardBoardAssemblyDto> Assemblies { get; set; }
        public ProccessCardBoardDto()
        {
            Assemblies = new List<ProccessCardBoardAssemblyDto>();
        }
    }
}
