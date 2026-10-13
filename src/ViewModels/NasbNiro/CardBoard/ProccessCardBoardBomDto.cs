using System;
using System.Collections.Generic;
using System.Text;

namespace ViewModels.NasbNiro.CardBoard
{
    public class ProccessCardBoardBomDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int? PredecessorId { get; set; }
        public List<ProccessCardBoardProccessDto> proccesses { get; set; }
        public ProccessCardBoardBomDto()
        {
            proccesses = new List<ProccessCardBoardProccessDto>();
        }
    }
}
