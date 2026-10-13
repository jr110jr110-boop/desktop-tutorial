using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.CardBoard
{
    public class ProccessCardBoardAssemblyDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public List<ProccessCardBoardAssemblyDto> assemblies { get; set; }
        public List<ProccessCardBoardBomDto> Boms { get; set; }
        public ProccessCardBoardAssemblyDto()
        {
            Boms = new List<ProccessCardBoardBomDto>();
        }
    }
}
