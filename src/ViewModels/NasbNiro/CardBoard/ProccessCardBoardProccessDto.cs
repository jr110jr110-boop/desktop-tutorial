using Entities.Model.NasbNiro.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using ViewModels.Constants;
using ViewModels.NasbNiro.ProcessDtos;

namespace ViewModels.NasbNiro.CardBoard
{
    public class ProccessCardBoardProccessDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ExitCommodityCode { get; set; }
        public string EnterCommodityCode { get; set; }
        public string DocumentCode { get; set; }
        public int WorkflowId { get; set; }
        public int MapDuration { get; set; }
        public int MasterDuration { get; set; }
        public int ProcessDuration { get; set; }
        public ProcessType Type { get; set; }
        public string TypeDescription
        {
            get
            {
                return Type.ToString();
            }
        }
        public ProcessStatus ProcessStatus { get; set; }
        public string ProcessStatusDescription
        {
            get
            {
                return ProcessStatusDescriptions.GetStatusDescription(ProcessStatus);
            }
        }
        public int Sort { get; set; }
        public DateTimeOffset? ProgramStartDate { get; set; }
        public DateTimeOffset? ProgramEndDate { get; set; }
        public DateTimeOffset? ActualStartDate { get; set; }
        public DateTimeOffset? ActualEndDate { get; set; }
        public DateTimeOffset? DocAcceptDate { get; set; }
        public DateTimeOffset? MasterAcceptDate { get; set; }
        public decimal ProgramPrice { get; set; }
        public decimal WeightPercent { get; set; }
        public List<ResponseProcessFileDto> Files { get; set; }
        public ProccessCardBoardProccessDto()
        {
            Files = new List<ResponseProcessFileDto>();
        }
    }
}
